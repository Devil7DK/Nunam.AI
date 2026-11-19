using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Parsing;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// Consumes this machine's <see cref="IndexJobKind.Parse"/> jobs: detects the file type, runs the
    /// matching parser, upserts the <see cref="IndexedFile"/>, and enqueues the follow-up AI job —
    /// a Describe when the parser produced a preview image (the DescribeWorker enqueues the Embed
    /// after it), otherwise an Embed directly. Pending Embed/Describe jobs for the path are cleared
    /// first so a re-parse doesn't pile up duplicates.
    /// </summary>
    public class ParseWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        IOptions<IndexerOptions> options,
        ILogger<ParseWorker> logger) : PollingWorker(logger)
    {
        private readonly IndexerOptions _options = options.Value;
        private readonly ILogger<ParseWorker> _logger = logger;

        protected override string Name => nameof(ParseWorker);
        protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(_options.IdlePollSeconds);
        protected override TimeSpan MinBackoff => TimeSpan.FromSeconds(_options.MinBackoffSeconds);
        protected override TimeSpan MaxBackoff => TimeSpan.FromSeconds(_options.MaxBackoffSeconds);

        protected override async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var requeued = await queue.RequeueStaleAsync(
                IndexJobKind.Parse, TimeSpan.FromMinutes(_options.StaleJobMinutes), machine.MachineId, cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Parse job(s)", requeued);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var detector = scope.ServiceProvider.GetRequiredService<FileTypeDetector>();
            var selector = scope.ServiceProvider.GetRequiredService<ParserSelector>();

            var jobs = await queue.ClaimAsync(IndexJobKind.Parse, _options.ParseBatchSize, machine.MachineId, cancellationToken);
            if (jobs.Count == 0)
            {
                return PollResult.NoWork;
            }

            foreach (var job in jobs)
            {
                try
                {
                    await ProcessAsync(db, queue, detector, selector, job, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Parse job {JobId} for {Path} failed", job.Id, job.Path);
                    Fail(job, ex.Message);
                }
            }

            // Not cancellable: finished parses must land even when shutdown starts mid-batch.
            await db.SaveChangesAsync(CancellationToken.None);
            return PollResult.DidWork;
        }

        private async Task ProcessAsync(
            NunamDbContext db, JobQueue queue, FileTypeDetector detector, ParserSelector selector,
            IndexJob job, CancellationToken cancellationToken)
        {
            job.Stage = IndexJobStage.Detect;

            var info = new FileInfo(job.Path);
            if (!info.Exists)
            {
                Fail(job, "File no longer exists.");
                return;
            }

            var fileType = detector.Detect(info);

            job.Stage = IndexJobStage.Parse;
            var parser = selector.Select(fileType, info.Extension);
            var outcome = await parser.ParseAsync(info, fileType, cancellationToken);

            job.Stage = IndexJobStage.Upsert;
            var file = await db.Files.FirstOrDefaultAsync(
                f => f.MachineId == job.MachineId && f.Path == job.Path, cancellationToken);

            if (file is null)
            {
                file = new IndexedFile { MachineId = job.MachineId, Path = job.Path };
                db.Files.Add(file);
            }

            file.FileName = info.Name;
            file.Extension = info.Extension;
            file.SizeBytes = info.Length;
            file.FileType = fileType;
            file.FileCreatedAt = info.CreationTimeUtc;
            file.FileModifiedAt = info.LastWriteTimeUtc;
            file.IndexedAt = DateTimeOffset.UtcNow;
            file.Content = CleanContent(outcome.Content);
            file.Metadata = outcome.Metadata;
            file.PreviewImage = outcome.PreviewImage;
            if (!outcome.NeedsDescribe)
            {
                file.Description = null; // nothing will be described any more; drop a stale one
            }

            await EnqueueFollowUpAsync(db, queue, job, outcome, cancellationToken);

            job.Status = IndexJobStatus.Done;
            job.Error = null;

            _logger.LogInformation(
                "Parsed {Path} (type={Type}, content={ContentLen} chars, preview={HasPreview}, describe={NeedsDescribe})",
                job.Path, fileType, outcome.Content?.Length ?? 0, outcome.PreviewImage is not null, outcome.NeedsDescribe);
        }

        /// <summary>
        /// Replaces any pending Embed/Describe jobs for this path with the one follow-up the new parse
        /// needs. Jobs already <c>Processing</c> are left alone: the AIWorker holds them and would fail
        /// its whole batch saving a deleted row. They finish against the old content, and the newer
        /// job queued here runs after them and overwrites the result.
        /// </summary>
        private static async Task EnqueueFollowUpAsync(
            NunamDbContext db, JobQueue queue, IndexJob job, ParseOutcome outcome, CancellationToken cancellationToken)
        {
            await db.IndexJobs
                .Where(j => j.MachineId == job.MachineId
                    && j.Path == job.Path
                    && (j.Kind == IndexJobKind.Embed || j.Kind == IndexJobKind.Describe)
                    && j.Status == IndexJobStatus.Pending)
                .ExecuteDeleteAsync(cancellationToken);

            queue.Enqueue(job.MachineId, job.Path, outcome.NeedsDescribe ? IndexJobKind.Describe : IndexJobKind.Embed);
        }

        /// <summary>Postgres text rejects NUL, and the full-text trigger rejects oversized content.</summary>
        private string? CleanContent(string? content) =>
            string.IsNullOrWhiteSpace(content) ? null : Truncate(content.Replace("\0", string.Empty), _options.MaxContentChars);

        private static void Fail(IndexJob job, string error)
        {
            job.Status = IndexJobStatus.Failed;
            job.Error = Truncate(error, 2000);
        }
    }
}
