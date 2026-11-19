using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Jobs;
using Nunam.Indexer.Indexing;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// Watches every configured path for ongoing changes via <see cref="FileSystemWatcher"/> and
    /// enqueues Parse jobs. Filesystem events are bursty (a single save can fire several
    /// <c>Changed</c> events), so paths are buffered and only enqueued once they've been quiet for a
    /// short debounce window. <see cref="JobQueue.EnqueueParseIfAbsentAsync"/> provides a second
    /// layer of de-duplication at the database.
    /// </summary>
    public class WatcherWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        PathFilter pathFilter,
        IOptions<IndexerOptions> options,
        ILogger<WatcherWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);
        private readonly IndexerOptions _options = options.Value;
        private readonly ConcurrentDictionary<string, DateTimeOffset> _pending = new(StringComparer.OrdinalIgnoreCase);

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
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                watcher.Created += OnChanged;
                watcher.Changed += OnChanged;
                watcher.Renamed += OnRenamed;
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
                var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();

                foreach (var path in due)
                {
                    // Drop from the buffer first; a new event arriving mid-enqueue re-adds it.
                    _pending.TryRemove(path, out _);

                    if (!File.Exists(path)) continue; // moved/deleted before we got to it

                    try
                    {
                        if (await queue.EnqueueParseIfAbsentAsync(machine.MachineId, path, stoppingToken))
                        {
                            logger.LogDebug("Enqueued Parse for changed file {Path}", path);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to enqueue Parse for {Path}; will retry on next event", path);
                    }
                }
            }
        }

        private void OnChanged(object sender, FileSystemEventArgs e) => Buffer(e.FullPath);

        private void OnRenamed(object sender, RenamedEventArgs e) => Buffer(e.FullPath);

        private void Buffer(string path)
        {
            if (pathFilter.IsExcluded(path)) return;
            if (Directory.Exists(path)) return; // directory event, not a file

            _pending[path] = DateTimeOffset.UtcNow;
        }

        private void OnError(object sender, ErrorEventArgs e) =>
            logger.LogError(e.GetException(), "FileSystemWatcher error; some changes may be missed until the next scan");
    }
}
