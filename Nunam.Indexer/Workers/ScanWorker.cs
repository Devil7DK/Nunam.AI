using Microsoft.Extensions.Options;
using Nunam.Core.Agents;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Management;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// One-shot startup crawl of every configured watch path: sends what it finds to the server in
    /// batches, which queues a Parse job for each file that is new or changed since it was last indexed
    /// (by size + modified time), then drops entries for files deleted while the indexer wasn't
    /// watching and for files the configuration no longer covers. Restarts are cheap: unchanged files
    /// are only compared.
    /// </summary>
    public class ScanWorker(
        IndexerServer server,
        FileTree fileTree,
        IndexScope indexScope,
        IndexerControl control,
        IOptions<IndexerOptions> options,
        ILogger<ScanWorker> logger) : BackgroundService
    {
        private const int BatchSize = 500;
        private readonly IndexerOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.WatchPaths.Length == 0)
            {
                logger.LogWarning("No WatchPaths configured; startup scan skipped.");
                return;
            }

            control.ScanStartedAt = DateTimeOffset.UtcNow;
            var enqueued = 0;
            foreach (var root in _options.WatchPaths)
            {
                if (stoppingToken.IsCancellationRequested) break;

                if (!Directory.Exists(root))
                {
                    logger.LogWarning("Watch path does not exist or the service can't open it, skipping: {Path}", root);
                    continue;
                }

                logger.LogInformation("Scanning {Path}", root);
                enqueued += await ScanRootAsync(root, stoppingToken);
            }

            var (missing, outOfScope) = await RemoveStaleAsync(stoppingToken);
            control.ScanFinishedAt = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "Startup scan complete. Enqueued {Count} Parse job(s), removed {Missing} missing file(s) and {OutOfScope} no longer in the watch paths.",
                enqueued, missing, outOfScope);
        }

        private async Task<int> ScanRootAsync(string root, CancellationToken cancellationToken)
        {
            var enqueued = 0;
            var batch = new List<ObservedFile>(BatchSize);

            foreach (var path in fileTree.EnumerateFiles(root))
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists) continue;
                    batch.Add(new ObservedFile(path, info.Length, info.LastWriteTimeUtc));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not stat {Path}; skipping", path);
                    continue;
                }

                if (batch.Count == BatchSize)
                {
                    enqueued += await server.ScanAsync(batch, cancellationToken);
                    batch.Clear();
                }
            }

            if (batch.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                enqueued += await server.ScanAsync(batch, cancellationToken);
            }
            return enqueued;
        }

        /// <summary>
        /// Removes entries for files that are gone, and for files a watch path or exclude pattern no
        /// longer covers. Entries under a watch path that is unreachable right now are kept: an unplugged
        /// drive or an offline share would otherwise look like every file on it was deleted.
        /// </summary>
        private async Task<(int Missing, int OutOfScope)> RemoveStaleAsync(CancellationToken cancellationToken)
        {
            var missing = new List<string>();
            var outOfScope = new List<string>();
            string? after = null;
            do
            {
                var page = await server.ListPathsAsync(after, 2000, cancellationToken);
                foreach (var path in page.Paths)
                {
                    if (indexScope.IsUnderUnavailableRoot(path)) continue;

                    if (!indexScope.Allows(path, out var reason))
                    {
                        logger.LogDebug("Removing {Path} from the index: {Reason}", path, reason);
                        outOfScope.Add(path);
                    }
                    else if (!File.Exists(path))
                    {
                        missing.Add(path);
                    }
                }
                after = page.Next;
            }
            while (after is not null && !cancellationToken.IsCancellationRequested);

            // Removed after paging, so the pages don't shift under the cursor.
            return (await RemoveAsync(missing, cancellationToken), await RemoveAsync(outOfScope, cancellationToken));
        }

        private async Task<int> RemoveAsync(List<string> paths, CancellationToken cancellationToken)
        {
            var removed = 0;
            foreach (var chunk in paths.Chunk(BatchSize))
            {
                if (cancellationToken.IsCancellationRequested) break;
                removed += await server.RemoveAsync([.. chunk], cancellationToken);
            }
            return removed;
        }
    }
}
