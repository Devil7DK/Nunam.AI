using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Jobs;
using Nunam.Indexer.Indexing;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// One-shot startup crawl of every configured watch path: enqueues a Parse job for each file
    /// that is new or has changed since it was last indexed (by size + modified time). Unchanged
    /// files and paths that already have a pending Parse job are skipped, so restarts are cheap.
    /// </summary>
    public class ScanWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        PathFilter pathFilter,
        IOptions<IndexerOptions> options,
        ILogger<ScanWorker> logger) : BackgroundService
    {
        private readonly IndexerOptions _options = options.Value;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.WatchPaths.Length == 0)
            {
                logger.LogWarning("No WatchPaths configured; startup scan skipped.");
                return;
            }

            var enqueued = 0;
            foreach (var root in _options.WatchPaths)
            {
                if (stoppingToken.IsCancellationRequested) break;

                if (!Directory.Exists(root))
                {
                    logger.LogWarning("Watch path does not exist, skipping: {Path}", root);
                    continue;
                }

                logger.LogInformation("Scanning {Path}", root);
                enqueued += await ScanRootAsync(root, stoppingToken);
            }

            logger.LogInformation("Startup scan complete. Enqueued {Count} Parse job(s).", enqueued);
        }

        private async Task<int> ScanRootAsync(string root, CancellationToken cancellationToken)
        {
            var enqueued = 0;

            foreach (var path in EnumerateFiles(root))
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (pathFilter.IsExcluded(path)) continue;

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (!info.Exists) continue;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not stat {Path}; skipping", path);
                    continue;
                }

                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
                var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();

                var existing = await db.Files
                    .Where(f => f.MachineId == machine.MachineId && f.Path == path)
                    .Select(f => new { f.SizeBytes, f.FileModifiedAt })
                    .FirstOrDefaultAsync(cancellationToken);

                if (existing is not null && IsUnchanged(existing.SizeBytes, existing.FileModifiedAt, info))
                {
                    continue;
                }

                if (await queue.EnqueueParseIfAbsentAsync(machine.MachineId, path, cancellationToken))
                {
                    enqueued++;
                }
            }

            return enqueued;
        }

        private static bool IsUnchanged(long? indexedSize, DateTimeOffset? indexedModified, FileInfo info) =>
            indexedSize == info.Length
            && indexedModified is not null
            && Math.Abs((indexedModified.Value - info.LastWriteTimeUtc).TotalSeconds) < 2;

        /// <summary>Depth-first enumeration that tolerates unreadable directories instead of aborting the scan.</summary>
        private IEnumerable<string> EnumerateFiles(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                if (pathFilter.IsExcluded(dir + Path.DirectorySeparatorChar)) continue;

                string[] subdirs;
                try
                {
                    subdirs = Directory.GetDirectories(dir);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Cannot enumerate directories under {Dir}; skipping", dir);
                    subdirs = [];
                }

                foreach (var sub in subdirs) stack.Push(sub);

                string[] files;
                try
                {
                    files = Directory.GetFiles(dir);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Cannot enumerate files in {Dir}; skipping", dir);
                    files = [];
                }

                foreach (var file in files) yield return file;
            }
        }
    }
}
