using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Nunam.API.Auth;
using Nunam.Core.Agents;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;
using Nunam.Core.Management;

namespace Nunam.API.Endpoints
{
    /// <summary>
    /// What a machine's indexer reads and writes, always limited to the machine its token belongs to.
    /// The indexer reads the files; deciding what to queue and what the AI worker does next happens here.
    /// </summary>
    public static class AgentIndexerEndpoints
    {
        private const int MaxBatch = 2000;
        private const int MaxPathPage = 5000;
        private const int MaxLogBatch = 1000;

        /// <summary>The full-text trigger fails on a tsvector over 1 MB; indexers cut content well below this.</summary>
        private const int MaxContentChars = 1_000_000;

        public static IEndpointRouteBuilder MapAgentIndexerEndpoints(this IEndpointRouteBuilder app)
        {
            var indexer = app.MapGroup("/api/agent/indexer").RequireAuthorization(Policies.Indexer);
            indexer.MapPost("/hello", HelloAsync);
            indexer.MapGet("/settings", GetSettingsAsync);
            indexer.MapGet("/poll", PollAsync);
            indexer.MapPut("/status", ReportStatusAsync);
            indexer.MapPost("/commands/{id:guid}", CompleteCommandAsync);
            indexer.MapPost("/logs", StoreLogsAsync);
            indexer.MapPost("/scan", ScanAsync);
            indexer.MapGet("/files", ListPathsAsync);
            indexer.MapPost("/files/remove", RemoveFilesAsync);
            indexer.MapPost("/parse-jobs", EnqueueAsync);
            indexer.MapPost("/parse-jobs/claim", ClaimAsync);
            indexer.MapPost("/parse-jobs/recover", RecoverAsync);
            indexer.MapPost("/parse-jobs/retry-failed", RetryFailedAsync);
            indexer.MapPost("/parse-jobs/{id:guid}/result", CompleteParseAsync);
            return app;
        }

        private static async Task<Results<Ok<IndexerHelloResponse>, ValidationProblem>> HelloAsync(
            IndexerHelloRequest request, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Hostname) || request.Hostname.Length > 255 || request.Name?.Length > 200)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["hostname"] = ["Send the hostname, and a name of up to 200 characters."] });
            }

            var machine = await db.Machines.AsTracking().FirstAsync(m => m.Id == agent.MachineId(), cancellationToken);
            machine.Hostname = request.Hostname.Trim();
            if (!string.IsNullOrWhiteSpace(request.Name)) machine.Name = request.Name.Trim();
            machine.LastSeen = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new IndexerHelloResponse(machine.Id, machine.Name));
        }

        private static async Task<IndexerSettingsResponse> GetSettingsAsync(
            ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            var row = await db.IndexerSettings
                .Where(s => s.MachineId == machineId)
                .Select(s => new { s.Settings, s.Revision })
                .FirstOrDefaultAsync(cancellationToken);
            return row is null ? new(null, null) : new(RemoteIndexerSettings.FromJson(row.Settings), row.Revision);
        }

        private static async Task<IndexerPollResponse> PollAsync(ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            var commands = await db.IndexerCommands
                .Where(c => c.MachineId == machineId && c.Status == IndexerCommandStatus.Pending)
                .OrderBy(c => c.Created)
                .Select(c => new PendingCommand(c.Id, c.Kind, c.RequestedBy))
                .ToListAsync(cancellationToken);
            var revision = await db.IndexerSettings
                .Where(s => s.MachineId == machineId)
                .Select(s => (int?)s.Revision)
                .FirstOrDefaultAsync(cancellationToken);
            return new IndexerPollResponse(commands, revision);
        }

        private static async Task<NoContent> ReportStatusAsync(
            IndexerStatusReport report, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            var status = await db.IndexerStatus.AsTracking().FirstOrDefaultAsync(s => s.MachineId == machineId, cancellationToken);
            if (status is null)
            {
                status = new IndexerStatus { MachineId = machineId };
                db.IndexerStatus.Add(status);
            }

            status.ReportedAt = DateTimeOffset.UtcNow;
            status.ProcessStartedAt = report.ProcessStartedAt;
            status.StartedAt = report.StartedAt;
            status.Version = Truncate(report.Version, 100);
            status.OperatingSystem = Truncate(report.OperatingSystem, 200);
            status.ServiceAccount = Truncate(report.ServiceAccount, 200);
            status.State = report.State;
            status.RemoteManagement = report.RemoteManagement;
            status.AppliedRevision = report.AppliedRevision;
            status.ScanStartedAt = report.ScanStartedAt;
            status.ScanFinishedAt = report.ScanFinishedAt;
            status.Config = JsonSerializer.Serialize(report.Config, RemoteIndexerSettings.Json);
            status.WatchPaths = JsonSerializer.Serialize(report.WatchPaths, RemoteIndexerSettings.Json);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.NoContent();
        }

        private static async Task<Results<NoContent, NotFound>> CompleteCommandAsync(
            Guid id, CommandResult result, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            var updated = await db.IndexerCommands
                .Where(c => c.Id == id && c.MachineId == machineId && c.Status == IndexerCommandStatus.Pending)
                .ExecuteUpdateAsync(c => c
                    .SetProperty(x => x.Status, result.Status)
                    .SetProperty(x => x.Result, Truncate(result.Result, 2000))
                    .SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.LastModified, DateTimeOffset.UtcNow), cancellationToken);
            return updated == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        }

        private static async Task<Results<NoContent, ValidationProblem>> StoreLogsAsync(
            List<LogRecord> records, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (records.Count > MaxLogBatch) return TooMany("records", MaxLogBatch);

            var machineId = agent.MachineId();
            db.IndexerLogs.AddRange(records.Select(r => new IndexerLogEntry
            {
                MachineId = machineId,
                At = r.At,
                Level = r.Level,
                Category = Truncate(r.Category, 200),
                Message = Truncate(r.Message, 4000),
                Exception = r.Exception is null ? null : Truncate(r.Exception, 8000),
            }));
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.NoContent();
        }

        /// <summary>
        /// Queues a Parse job for each file the scan found that is new or changed since it was indexed
        /// (by size and modified time), unless one is already pending.
        /// </summary>
        private static async Task<Results<Ok<CountResponse>, ValidationProblem>> ScanAsync(
            ScanBatchRequest request, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (request.Files.Count > MaxBatch) return TooMany("files", MaxBatch);

            var machineId = agent.MachineId();
            var paths = request.Files.Select(f => f.Path).Distinct().ToList();
            var indexed = await db.Files
                .Where(f => f.MachineId == machineId && paths.Contains(f.Path))
                .Select(f => new { f.Path, f.SizeBytes, f.FileModifiedAt })
                .ToDictionaryAsync(f => f.Path, cancellationToken);

            var changed = request.Files
                .Where(f => !indexed.TryGetValue(f.Path, out var i) || !IsUnchanged(i.SizeBytes, i.FileModifiedAt, f))
                .Select(f => f.Path)
                .Distinct()
                .ToList();
            return TypedResults.Ok(new CountResponse(await EnqueueParseAsync(db, machineId, changed, cancellationToken)));
        }

        private static bool IsUnchanged(long? indexedSize, DateTimeOffset? indexedModified, ObservedFile file) =>
            indexedSize == file.SizeBytes
            && indexedModified is not null
            && Math.Abs((indexedModified.Value - file.ModifiedAt).TotalSeconds) < 2;

        private static async Task<Results<Ok<CountResponse>, ValidationProblem>> EnqueueAsync(
            EnqueueParseRequest request, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (request.Paths.Count > MaxBatch) return TooMany("paths", MaxBatch);
            return TypedResults.Ok(new CountResponse(
                await EnqueueParseAsync(db, agent.MachineId(), [.. request.Paths.Distinct()], cancellationToken)));
        }

        /// <summary>
        /// A burst of changes to one path collapses to one pending job. A <c>Processing</c> job doesn't
        /// count: it may have read the file before this change.
        /// </summary>
        private static async Task<int> EnqueueParseAsync(NunamDbContext db, Guid machineId, List<string> paths, CancellationToken cancellationToken)
        {
            if (paths.Count == 0) return 0;

            var pending = await db.IndexJobs
                .Where(j => j.MachineId == machineId && j.Kind == IndexJobKind.Parse && j.Status == IndexJobStatus.Pending
                    && paths.Contains(j.Path))
                .Select(j => j.Path)
                .ToHashSetAsync(cancellationToken);

            var queue = new JobQueue(db);
            var added = 0;
            foreach (var path in paths.Where(p => !pending.Contains(p) && p.Length > 0))
            {
                queue.Enqueue(machineId, path, IndexJobKind.Parse);
                added++;
            }
            await db.SaveChangesAsync(cancellationToken);
            return added;
        }

        private static async Task<IndexedPathPage> ListPathsAsync(
            ClaimsPrincipal agent, NunamDbContext db, string? after, int? limit, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            var take = Math.Clamp(limit ?? 1000, 1, MaxPathPage);
            var paths = await db.Files
                .Where(f => f.MachineId == machineId && (after == null || string.Compare(f.Path, after) > 0))
                .OrderBy(f => f.Path)
                .Select(f => f.Path)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new IndexedPathPage(paths, paths.Count == take ? paths[^1] : null);
        }

        /// <summary>
        /// Removes each path's entry and, since a delete event doesn't say whether it was a file or a
        /// folder, every entry under it. Jobs a worker is processing are left alone: it fails them itself
        /// once it finds the file row gone.
        /// </summary>
        private static async Task<Results<Ok<CountResponse>, ValidationProblem>> RemoveFilesAsync(
            RemoveFilesRequest request, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (request.Paths.Count > MaxBatch) return TooMany("paths", MaxBatch);
            if (request.Separator is not ('/' or '\\'))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["separator"] = ["Use / or \\."] });
            }

            var machineId = agent.MachineId();
            var removed = 0;
            foreach (var path in request.Paths.Where(p => p.Length > 0).Distinct())
            {
                var folderPrefix = path.TrimEnd(request.Separator) + request.Separator;
                await db.IndexJobs
                    .Where(j => j.MachineId == machineId
                        && j.Status != IndexJobStatus.Processing
                        && (j.Path == path || j.Path.StartsWith(folderPrefix)))
                    .ExecuteDeleteAsync(cancellationToken);
                removed += await db.Files
                    .Where(f => f.MachineId == machineId && (f.Path == path || f.Path.StartsWith(folderPrefix)))
                    .ExecuteDeleteAsync(cancellationToken);
            }
            return TypedResults.Ok(new CountResponse(removed));
        }

        private static async Task<List<ClaimedParseJob>> ClaimAsync(
            ClaimRequest request, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var jobs = await new JobQueue(db).ClaimAsync(
                IndexJobKind.Parse, Math.Clamp(request.BatchSize, 1, 100), agent.MachineId(), cancellationToken);
            return [.. jobs.Select(j => new ClaimedParseJob(j.Id, j.Path, j.Attempts))];
        }

        private static async Task<StaleRecovery> RecoverAsync(
            RecoverRequest request, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken) =>
            await new JobQueue(db).RecoverStaleAsync(
                IndexJobKind.Parse, TimeSpan.FromMinutes(Math.Max(1, request.StaleMinutes)), Math.Max(1, request.MaxAttempts),
                agent.MachineId(), cancellationToken);

        private static async Task<CountResponse> RetryFailedAsync(ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            return new CountResponse(await db.IndexJobs
                .Where(j => j.MachineId == machineId && j.Kind == IndexJobKind.Parse && j.Status == IndexJobStatus.Failed)
                .ExecuteUpdateAsync(j => j
                    .SetProperty(x => x.Status, IndexJobStatus.Pending)
                    .SetProperty(x => x.Attempts, 0)
                    .SetProperty(x => x.Error, (string?)null)
                    .SetProperty(x => x.LastModified, DateTimeOffset.UtcNow), cancellationToken));
        }

        /// <summary>
        /// Settles a claimed Parse job. A parsed file is upserted and gets the one AI job its new content
        /// needs. 409 means the job is no longer this indexer's: stale recovery gave it back to the queue.
        /// </summary>
        private static async Task<Results<NoContent, NotFound, Conflict, ValidationProblem>> CompleteParseAsync(
            Guid id, ParseResult result, ClaimsPrincipal agent, NunamDbContext db, CancellationToken cancellationToken)
        {
            var machineId = agent.MachineId();
            var job = await db.IndexJobs.AsTracking()
                .FirstOrDefaultAsync(j => j.Id == id && j.MachineId == machineId && j.Kind == IndexJobKind.Parse, cancellationToken);
            if (job is null) return TypedResults.NotFound();
            if (job.Status != IndexJobStatus.Processing) return TypedResults.Conflict();

            job.Stage = result.Stage;
            switch (result.Outcome)
            {
                case JobOutcome.Done when result.File is { } parsed:
                    await UpsertAsync(db, job, parsed, cancellationToken);
                    job.Status = IndexJobStatus.Done;
                    job.Error = null;
                    break;
                case JobOutcome.Done:
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["file"] = ["A finished job needs its file."] });
                case JobOutcome.Failed:
                    job.Status = IndexJobStatus.Failed;
                    job.Error = Truncate(result.Error ?? "Failed.", 2000);
                    break;
                case JobOutcome.Retry:
                    job.Status = IndexJobStatus.Pending;
                    break;
                case JobOutcome.Returned:
                    job.ReturnToQueue();
                    break;
            }

            // Not cancellable: a finished parse must land even when the indexer hangs up.
            await db.SaveChangesAsync(CancellationToken.None);
            return TypedResults.NoContent();
        }

        private static async Task UpsertAsync(NunamDbContext db, IndexJob job, ParsedFile parsed, CancellationToken cancellationToken)
        {
            var file = await db.Files.AsTracking()
                .FirstOrDefaultAsync(f => f.MachineId == job.MachineId && f.Path == job.Path, cancellationToken);
            if (file is null)
            {
                file = new IndexedFile { MachineId = job.MachineId, Path = job.Path };
                db.Files.Add(file);
            }

            // Postgres text rejects NUL.
            var content = string.IsNullOrWhiteSpace(parsed.Content) ? null : Truncate(parsed.Content.Replace("\0", string.Empty), MaxContentChars);
            var followUp = FollowUpFor(file, parsed.FileName, content, parsed);

            file.FileName = parsed.FileName;
            file.Extension = parsed.Extension;
            file.SizeBytes = parsed.SizeBytes;
            file.FileType = parsed.FileType;
            file.FileCreatedAt = parsed.CreatedAt;
            file.FileModifiedAt = parsed.ModifiedAt;
            file.IndexedAt = DateTimeOffset.UtcNow;
            file.Content = content;
            file.Metadata = string.IsNullOrWhiteSpace(parsed.Metadata) ? null : parsed.Metadata;
            file.PreviewImage = parsed.PreviewImage;
            if (!parsed.NeedsDescribe)
            {
                file.Description = null; // nothing will be described any more; drop a stale one
            }

            if (followUp is { } kind)
            {
                // Jobs already Processing are left alone: the AI worker holds them. They finish against the
                // old content, and the job queued here runs after them and overwrites the result.
                await db.IndexJobs
                    .Where(j => j.MachineId == job.MachineId
                        && j.Path == job.Path
                        && (j.Kind == IndexJobKind.Embed || j.Kind == IndexJobKind.Describe)
                        && j.Status == IndexJobStatus.Pending)
                    .ExecuteDeleteAsync(cancellationToken);
                new JobQueue(db).Enqueue(job.MachineId, job.Path, kind);
            }
        }

        /// <summary>
        /// The AI job the new parse needs, judged against the row before it is overwritten. A file saved
        /// again without changes (or only touched) keeps its description and embedding, which cost GPU
        /// time to redo.
        /// </summary>
        private static IndexJobKind? FollowUpFor(IndexedFile file, string fileName, string? content, ParsedFile parsed)
        {
            if (parsed.NeedsDescribe)
            {
                var samePreview = file.PreviewImage is not null && parsed.PreviewImage is not null
                    && file.PreviewImage.AsSpan().SequenceEqual(parsed.PreviewImage);
                if (file.Description is null || !samePreview) return IndexJobKind.Describe;
            }

            var embedInputChanged = file.FileName != fileName
                || file.Content != content
                || (!parsed.NeedsDescribe && file.Description is not null);
            return file.Embedding is null || embedInputChanged ? IndexJobKind.Embed : null;
        }

        private static ValidationProblem TooMany(string field, int max) =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [$"Send at most {max} at a time."] });

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
