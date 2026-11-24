using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// Watches every configured path with <see cref="FileSystemWatcher"/>: new and changed files get a
    /// Parse job, deleted or moved-away ones are dropped from the index, and a folder moved in has its
    /// files queued. Events are bursty and editors save by delete-then-rename, so paths are buffered
    /// and only acted on once quiet for <see cref="DebounceWindow"/>, judged by what is on disk then.
    /// </summary>
    public class WatcherWorker(
        IndexerServer server,
        PathFilter pathFilter,
        FileTree fileTree,
        IndexScope scope,
        IOptions<IndexerOptions> options,
        ILogger<WatcherWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
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
                    logger.LogWarning("Watch path does not exist or the service can't open it, not watching: {Path}", root);
                    continue;
                }

                foreach (var (folder, recursive) in WatchPlan.For(root, pathFilter))
                {
                    try
                    {
                        watchers.Add(Watch(folder, recursive));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    {
                        logger.LogWarning(ex, "Cannot watch {Path}; its changes are picked up at the next start", folder);
                    }
                }

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

                // Dropped from the buffer first; a new event arriving meanwhile re-adds it.
                foreach (var path in due) _pending.TryRemove(path, out _);

                try
                {
                    await HandleAsync(due, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not send {Count} change(s) to the server; trying again in {Seconds} s",
                        due.Count, RetryDelay.TotalSeconds);
                    foreach (var path in due) _pending.TryAdd(path, DateTimeOffset.UtcNow + RetryDelay - DebounceWindow);
                }
            }
        }

        private async Task HandleAsync(List<string> paths, CancellationToken cancellationToken)
        {
            var changed = new List<string>();
            var removed = new List<string>();
            foreach (var path in paths)
            {
                var exists = File.Exists(path) || Directory.Exists(path);
                if (exists && !scope.Allows(path, out var reason))
                {
                    logger.LogDebug("Ignoring change to {Path}: {Reason}", path, reason);
                }
                else if (File.Exists(path))
                {
                    changed.Add(path);
                }
                else if (Directory.Exists(path))
                {
                    // A folder moved or copied in arrives as one event; its files raise none of their own.
                    var files = fileTree.EnumerateFiles(path).ToList();
                    changed.AddRange(files);
                    if (files.Count > 0) logger.LogInformation("Found {Count} file(s) in folder {Path}", files.Count, path);
                }
                else
                {
                    removed.Add(path);
                }
            }

            foreach (var chunk in changed.Chunk(500))
            {
                var queued = await server.EnqueueParseAsync([.. chunk], cancellationToken);
                logger.LogDebug("Queued {Count} changed file(s) for parsing", queued);
            }
            foreach (var chunk in removed.Chunk(500))
            {
                var count = await server.RemoveAsync([.. chunk], cancellationToken);
                if (count > 0) logger.LogInformation("Removed {Count} file(s) under {Paths} from the index", count, string.Join(", ", chunk.Take(3)));
            }
        }

        private FileSystemWatcher Watch(string folder, bool recursive)
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
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
            return watcher;
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
