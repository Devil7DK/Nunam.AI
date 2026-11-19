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
    /// entries for files deleted while the indexer wasn't watching. Unchanged files and paths that
    /// already have a pending Parse job are skipped, so restarts are cheap.
    /// </summary>
    public class ScanWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        FileTree fileTree,
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

            var removed = await RemoveMissingAsync(stoppingToken);
            logger.LogInformation(
                "Startup scan complete. Enqueued {Count} Parse job(s), removed {Removed} missing file(s).", enqueued, removed);
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
        /// Only entries under watch paths that exist right now are checked: an unplugged drive or an
        /// unreachable share would otherwise look like every file on it was deleted.
        /// </summary>
        private async Task<int> RemoveMissingAsync(CancellationToken cancellationToken)
        {
            var roots = _options.WatchPaths
                .Where(Directory.Exists)
                .Select(r => r.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .ToArray();
            if (roots.Length == 0) return 0;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();

            var indexed = await db.Files
                .Where(f => f.MachineId == machine.MachineId)
                .Select(f => f.Path)
                .ToListAsync(cancellationToken);

            var removed = 0;
            foreach (var path in indexed)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!roots.Any(r => path.StartsWith(r, StringComparison.Ordinal)) || File.Exists(path)) continue;

                removed += await IndexRemover.RemoveAsync(db, machine.MachineId, path, cancellationToken);
            }

            return removed;
        }

        private static bool IsUnchanged(long? indexedSize, DateTimeOffset? indexedModified, FileInfo info) =>
            indexedSize == info.Length
            && indexedModified is not null
            && Math.Abs((indexedModified.Value - info.LastWriteTimeUtc).TotalSeconds) < 2;
    }
}
