using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Jobs;
using Nunam.Indexer.Indexing;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// Watches every configured path with <see cref="FileSystemWatcher"/>: new and changed files get a
    /// Parse job, deleted or moved-away ones are dropped from the index, and a folder moved in has its
    /// files queued. Events are bursty and editors save by delete-then-rename, so paths are buffered
    /// and only acted on once quiet for <see cref="DebounceWindow"/>, judged by what is on disk then.
    /// </summary>
    public class WatcherWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        PathFilter pathFilter,
        FileTree fileTree,
        IndexScope scope,
        IOptions<IndexerOptions> options,
        ILogger<WatcherWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);
        private readonly IndexerOptions _options = options.Value;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _pending = new(PathComparer);

        private static StringComparer PathComparer =>
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var watchers = new List<FileSystemWatcher>();

            foreach (var root in _options.WatchPaths)
            {
                if (!Directory.Exists(root))
                {
                    logger.LogWarning("Watch path does not exist, not watching: {Path}", root);
                    continue;
                }

                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => Buffer(e.FullPath);
                watcher.Changed += (_, e) => Buffer(e.FullPath);
                watcher.Deleted += (_, e) => Buffer(e.FullPath);
                watcher.Renamed += (_, e) =>
                {
                    Buffer(e.OldFullPath);
                    Buffer(e.FullPath);
                };
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);

                logger.LogInformation("Watching {Path}", root);
            }

            try
            {
                await DrainLoopAsync(stoppingToken);
            }
            finally
            {
                foreach (var watcher in watchers) watcher.Dispose();
            }
        }

        private async Task DrainLoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var due = _pending
                    .Where(kv => DateTimeOffset.UtcNow - kv.Value >= DebounceWindow)
                    .Select(kv => kv.Key)
                    .ToList();

                if (due.Count == 0) continue;

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
                var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();

                foreach (var path in due)
                {
                    // Drop from the buffer first; a new event arriving mid-handling re-adds it.
                    _pending.TryRemove(path, out _);

                    try
                    {
                        await HandleAsync(db, queue, path, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Could not update the index for {Path}; the next scan will catch it", path);
                    }
                }
            }
        }

        private async Task HandleAsync(NunamDbContext db, JobQueue queue, string path, CancellationToken cancellationToken)
        {
            var exists = File.Exists(path) || Directory.Exists(path);
            if (exists && !scope.Allows(path, out var reason))
            {
                logger.LogDebug("Ignoring change to {Path}: {Reason}", path, reason);
                return;
            }

            if (File.Exists(path))
            {
                if (await queue.EnqueueParseIfAbsentAsync(machine.MachineId, path, cancellationToken))
                {
                    logger.LogDebug("Enqueued Parse for changed file {Path}", path);
                }
            }
            else if (Directory.Exists(path))
            {
                // A folder moved or copied in arrives as one event; its files raise none of their own.
                var queued = 0;
                foreach (var file in fileTree.EnumerateFiles(path))
                {
                    if (await queue.EnqueueParseIfAbsentAsync(machine.MachineId, file, cancellationToken)) queued++;
                }
                if (queued > 0) logger.LogInformation("Enqueued {Count} file(s) from folder {Path}", queued, path);
            }
            else
            {
                var removed = await IndexRemover.RemoveAsync(db, machine.MachineId, path, cancellationToken);
                if (removed > 0) logger.LogInformation("Removed {Count} file(s) under {Path} from the index", removed, path);
            }
        }

        private void Buffer(string path)
        {
            if (pathFilter.IsExcluded(path)) return;
            _pending[path] = DateTimeOffset.UtcNow;
        }

        private void OnError(object sender, ErrorEventArgs e) =>
            logger.LogError(e.GetException(), "FileSystemWatcher error; some changes may be missed until the next scan");
    }
}
