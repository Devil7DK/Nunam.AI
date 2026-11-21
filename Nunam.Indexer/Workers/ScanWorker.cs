using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Jobs;
using Nunam.Indexer.Indexing;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// One-shot startup crawl of every configured watch path: enqueues a Parse job for each file
    /// that is new or has changed since it was last indexed (by size + modified time), then drops
    /// entries for files deleted while the indexer wasn't watching and for files the configuration no
    /// longer covers. Unchanged files and paths that already have a pending Parse job are skipped,
    /// so restarts are cheap.
    /// </summary>
    public class ScanWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        FileTree fileTree,
        IndexScope indexScope,
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
                    logger.LogWarning("Watch path does not exist or the service can't open it, skipping: {Path}", root);
                    continue;
                }

                logger.LogInformation("Scanning {Path}", root);
                enqueued += await ScanRootAsync(root, stoppingToken);
            }

            var (missing, outOfScope) = await RemoveStaleAsync(stoppingToken);
            logger.LogInformation(
                "Startup scan complete. Enqueued {Count} Parse job(s), removed {Missing} missing file(s) and {OutOfScope} no longer in the watch paths.",
                enqueued, missing, outOfScope);
        }

        private async Task<int> ScanRootAsync(string root, CancellationToken cancellationToken)
        {
            var enqueued = 0;

            foreach (var path in fileTree.EnumerateFiles(root))
            {
                if (cancellationToken.IsCancellationRequested) break;

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

        /// <summary>
        /// Removes entries for files that are gone, and for files a watch path or exclude pattern no
        /// longer covers. Entries under a watch path that is unreachable right now are kept: an unplugged
        /// drive or an offline share would otherwise look like every file on it was deleted.
        /// </summary>
        private async Task<(int Missing, int OutOfScope)> RemoveStaleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();

            var indexed = await db.Files
                .Where(f => f.MachineId == machine.MachineId)
                .Select(f => f.Path)
                .ToListAsync(cancellationToken);

            int missing = 0, outOfScope = 0;
            foreach (var path in indexed)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (indexScope.IsUnderUnavailableRoot(path)) continue;

                if (!indexScope.Allows(path, out var reason))
                {
                    logger.LogDebug("Removing {Path} from the index: {Reason}", path, reason);
                    outOfScope += await IndexRemover.RemoveAsync(db, machine.MachineId, path, cancellationToken);
                }
                else if (!File.Exists(path))
                {
                    missing += await IndexRemover.RemoveAsync(db, machine.MachineId, path, cancellationToken);
                }
            }

            return (missing, outOfScope);
        }

        private static bool IsUnchanged(long? indexedSize, DateTimeOffset? indexedModified, FileInfo info) =>
            indexedSize == info.Length
            && indexedModified is not null
            && Math.Abs((indexedModified.Value - info.LastWriteTimeUtc).TotalSeconds) < 2;
    }
}
