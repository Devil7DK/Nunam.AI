using Microsoft.EntityFrameworkCore;
using Nunam.Core.Data;
using Nunam.Core.Entities;

namespace Nunam.Core.Jobs
{
    /// <summary>
    /// Postgres-backed work queue over the <c>index_jobs</c> table. Claims are atomic via
    /// <c>FOR UPDATE SKIP LOCKED</c>, so several workers — or several instances of the same
    /// worker — can poll the same <see cref="IndexJobKind"/> concurrently without ever
    /// grabbing the same row twice.
    /// </summary>
    public class JobQueue(NunamDbContext db)
    {
        /// <summary>
        /// Atomically transitions up to <paramref name="batchSize"/> of the oldest
        /// <see cref="IndexJobStatus.Pending"/> jobs of the given <paramref name="kind"/> to
        /// <see cref="IndexJobStatus.Processing"/> and returns them as tracked entities ready
        /// to be updated and saved. Rows locked by another worker are skipped, not waited on.
        /// </summary>
        /// <param name="machineId">
        /// Restricts the claim to one machine's jobs. Parse jobs must pass this: only the indexer
        /// running on the machine that owns the path can read the file.
        /// </param>
        /// <remarks>
        /// The status literals are the enum names because <c>IndexJob.Status</c>/<c>Kind</c> are
        /// stored via <c>HasConversion&lt;string&gt;()</c>. No LINQ operators are chained after
        /// <c>FromSqlInterpolated</c>, so EF runs the <c>UPDATE … RETURNING</c> verbatim rather
        /// than wrapping it in a (invalid) subquery.
        /// </remarks>
        public Task<List<IndexJob>> ClaimAsync(
            IndexJobKind kind, int batchSize, Guid? machineId = null, CancellationToken cancellationToken = default)
        {
            var kindName = kind.ToString();

            return db.IndexJobs
                .FromSqlInterpolated($"""
                    UPDATE index_jobs
                    SET status = 'Processing', last_modified = now()
                    WHERE id IN (
                        SELECT id FROM index_jobs
                        WHERE kind = {kindName} AND status = 'Pending'
                          AND ({machineId}::uuid IS NULL OR machine_id = {machineId}::uuid)
                        ORDER BY created
                        LIMIT {batchSize}
                        FOR UPDATE SKIP LOCKED
                    )
                    RETURNING *
                    """)
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// Returns <see cref="IndexJobStatus.Processing"/> jobs that have not been touched for
        /// <paramref name="staleAfter"/> to <see cref="IndexJobStatus.Pending"/>. A worker that
        /// crashed or was killed mid-batch leaves its claimed rows in <c>Processing</c>; without
        /// this they would never be picked up again. Returns the number of jobs requeued.
        /// </summary>
        public Task<int> RequeueStaleAsync(
            IndexJobKind kind, TimeSpan staleAfter, Guid? machineId = null, CancellationToken cancellationToken = default)
        {
            var cutoff = DateTimeOffset.UtcNow - staleAfter;

            return db.IndexJobs
                .Where(j => j.Kind == kind
                    && j.Status == IndexJobStatus.Processing
                    && j.LastModified < cutoff
                    && (machineId == null || j.MachineId == machineId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, IndexJobStatus.Pending)
                    .SetProperty(j => j.LastModified, DateTimeOffset.UtcNow),
                    cancellationToken);
        }

        /// <summary>
        /// Deletes <see cref="IndexJobStatus.Done"/> jobs last touched before <paramref name="olderThan"/>
        /// ago. Every parse, describe and embed leaves one, so without this the table only grows.
        /// Failed jobs stay: they are the record of what needs attention.
        /// </summary>
        public Task<int> PurgeDoneAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
        {
            var cutoff = DateTimeOffset.UtcNow - olderThan;
            return db.IndexJobs
                .Where(j => j.Status == IndexJobStatus.Done && j.LastModified < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }

        /// <summary>Adds a job of the given kind for a file. Saved with the caller's next <c>SaveChanges</c>.</summary>
        public void Enqueue(Guid machineId, string path, IndexJobKind kind) =>
            db.IndexJobs.Add(new IndexJob { MachineId = machineId, Path = path, Kind = kind });

        /// <summary>
        /// Enqueues a <see cref="IndexJobKind.Parse"/> job for the given file path unless a
        /// <see cref="IndexJobStatus.Pending"/> one already exists, so a burst of filesystem events for
        /// one path collapses to a single job. A <see cref="IndexJobStatus.Processing"/> job doesn't
        /// count: it may have read the file before this change. Returns <c>true</c> when a job was added.
        /// </summary>
        public async Task<bool> EnqueueParseIfAbsentAsync(
            Guid machineId, string path, CancellationToken cancellationToken = default)
        {
            var alreadyQueued = await db.IndexJobs.AnyAsync(
                j => j.MachineId == machineId
                    && j.Path == path
                    && j.Kind == IndexJobKind.Parse
                    && j.Status == IndexJobStatus.Pending,
                cancellationToken);

            if (alreadyQueued)
            {
                return false;
            }

            Enqueue(machineId, path, IndexJobKind.Parse);
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
