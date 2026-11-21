using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;
using Nunam.Indexer.Abstractions;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Management;
using Nunam.Indexer.Parsing;
using Nunam.Indexer.Providers;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// Consumes this machine's <see cref="IndexJobKind.Parse"/> jobs: detects the file type, runs the
    /// parsers configured for it until one reads it, upserts the <see cref="IndexedFile"/>, and enqueues the follow-up AI job when
    /// its input changed — a Describe for a new preview image (the DescribeWorker enqueues the Embed
    /// after it), otherwise an Embed. Pending Embed/Describe jobs for the path are cleared first so a
    /// re-parse doesn't pile up duplicates.
    /// </summary>
    public class ParseWorker(
        IServiceScopeFactory scopeFactory,
        MachineContext machine,
        IndexScope scope,
        IndexerControl control,
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
            var (requeued, failed) = await queue.RecoverStaleAsync(
                IndexJobKind.Parse, TimeSpan.FromMinutes(_options.StaleJobMinutes), _options.MaxAttempts, machine.MachineId, cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Parse job(s)", requeued);
            if (failed > 0) _logger.LogError("Gave up on {Count} Parse job(s) that stopped the worker {Max} times", failed, _options.MaxAttempts);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            if (control.Paused) return PollResult.NoWork;

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var detector = scope.ServiceProvider.GetRequiredService<FileTypeDetector>();
            var providers = scope.ServiceProvider.GetRequiredService<ProviderRegistry>();

            var jobs = await queue.ClaimAsync(IndexJobKind.Parse, _options.ParseBatchSize, machine.MachineId, cancellationToken);
            if (jobs.Count == 0)
            {
                return PollResult.NoWork;
            }

            foreach (var job in jobs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    ReturnUnfinished(job);
                    continue;
                }

                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(_options.ParseTimeoutSeconds));
                try
                {
                    await ProcessAsync(db, queue, detector, providers, job, deadline.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReturnUnfinished(job);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    _logger.LogWarning("Parsing {Path} took longer than {Seconds}s; giving up on it", job.Path, _options.ParseTimeoutSeconds);
                    Fail(job, $"Parsing took longer than {_options.ParseTimeoutSeconds} seconds.");
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
            NunamDbContext db, JobQueue queue, FileTypeDetector detector, ProviderRegistry providers,
            IndexJob job, CancellationToken cancellationToken)
        {
            job.Stage = IndexJobStage.Detect;

            if (!scope.Allows(job.Path, out var reason))
            {
                Fail(job, reason);
                return;
            }

            var info = new FileInfo(job.Path);
            if (!info.Exists)
            {
                Fail(job, "File no longer exists.");
                return;
            }

            var fileType = detector.Detect(info);

            job.Stage = IndexJobStage.Parse;
            var (outcome, parserName) = await ParseAsync(providers, info, (FileKind)fileType, cancellationToken);

            job.Stage = IndexJobStage.Upsert;
            var file = await db.Files.FirstOrDefaultAsync(
                f => f.MachineId == job.MachineId && f.Path == job.Path, cancellationToken);

            if (file is null)
            {
                file = new IndexedFile { MachineId = job.MachineId, Path = job.Path };
                db.Files.Add(file);
            }

            var content = CleanContent(outcome.Content);
            var followUp = FollowUpFor(file, info.Name, content, outcome);

            file.FileName = info.Name;
            file.Extension = info.Extension;
            file.SizeBytes = info.Length;
            file.FileType = fileType;
            file.FileCreatedAt = info.CreationTimeUtc;
            file.FileModifiedAt = info.LastWriteTimeUtc;
            file.IndexedAt = DateTimeOffset.UtcNow;
            file.Content = content;
            file.Metadata = outcome.Metadata is { Count: > 0 } metadata ? JsonSerializer.Serialize(metadata) : null;
            file.PreviewImage = outcome.PreviewImage;
            if (!outcome.NeedsDescribe)
            {
                file.Description = null; // nothing will be described any more; drop a stale one
            }

            if (followUp is { } kind)
            {
                await EnqueueFollowUpAsync(db, queue, job, kind, cancellationToken);
            }

            job.Status = IndexJobStatus.Done;
            job.Error = null;

            _logger.LogInformation(
                "Parsed {Path} with {Parser} (type={Type}, content={ContentLen} chars, preview={HasPreview}, follow-up={FollowUp})",
                job.Path, parserName, fileType, outcome.Content?.Length ?? 0, outcome.PreviewImage is not null, followUp?.ToString() ?? "none");
        }

        /// <summary>
        /// Tries each parser in the file's chain until one neither declines nor fails. When one failed and
        /// only the name-only fallback was left, the file's job fails with that error instead, so a file a
        /// parser broke on shows up as failed rather than as indexed without content.
        /// </summary>
        private async Task<(ParseOutcome Outcome, string Parser)> ParseAsync(
            ProviderRegistry providers, FileInfo info, FileKind kind, CancellationToken cancellationToken)
        {
            var request = new ParseRequest(
                info, kind,
                new ParseLimits(_options.MaxFileSizeBytes, _options.MaxContentChars, _options.PreviewMaxEdgePx, _options.MaxImagePixels),
                providers.Ocr, providers.PdfRenderer);

            Exception? firstError = null;
            foreach (var parser in providers.ChainFor(kind, info.Extension))
            {
                if (firstError is not null && parser.Name == Core.Management.IndexerProviders.Parsers.Fallback) break;
                try
                {
                    var outcome = await parser.ParseAsync(request, cancellationToken);
                    if (outcome.IsDeclined) continue;
                    if (!providers.IsBuiltIn(parser) && outcome.PreviewImage is { } raw)
                    {
                        var preview = PreviewImages.Create(raw, _options.PreviewMaxEdgePx, _options.MaxImagePixels)?.Bytes;
                        outcome = outcome with { PreviewImage = preview, NeedsDescribe = outcome.NeedsDescribe && preview is not null };
                    }
                    return (outcome, parser.Name);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "{Parser} could not read {Path}", parser.Name, info.FullName);
                    firstError ??= ex;
                }
            }

            throw firstError ?? new InvalidOperationException("No parser read the file.");
        }

        /// <summary>
        /// The AI job the new parse needs, judged against the row before it is overwritten. A file saved
        /// again without changes (or only touched) keeps its description and embedding, which cost GPU
        /// time to redo.
        /// </summary>
        private static IndexJobKind? FollowUpFor(IndexedFile file, string fileName, string? content, ParseOutcome outcome)
        {
            if (outcome.NeedsDescribe)
            {
                var samePreview = file.PreviewImage is not null && outcome.PreviewImage is not null
                    && file.PreviewImage.AsSpan().SequenceEqual(outcome.PreviewImage);
                if (file.Description is null || !samePreview) return IndexJobKind.Describe;
            }

            var embedInputChanged = file.FileName != fileName
                || file.Content != content
                || (!outcome.NeedsDescribe && file.Description is not null);
            return file.Embedding is null || embedInputChanged ? IndexJobKind.Embed : null;
        }

        /// <summary>
        /// Replaces any pending Embed/Describe jobs for this path with the one follow-up the new parse
        /// needs. Jobs already <c>Processing</c> are left alone: the AIWorker holds them and would fail
        /// its whole batch saving a deleted row. They finish against the old content, and the newer
        /// job queued here runs after them and overwrites the result.
        /// </summary>
        private static async Task EnqueueFollowUpAsync(
            NunamDbContext db, JobQueue queue, IndexJob job, IndexJobKind kind, CancellationToken cancellationToken)
        {
            await db.IndexJobs
                .Where(j => j.MachineId == job.MachineId
                    && j.Path == job.Path
                    && (j.Kind == IndexJobKind.Embed || j.Kind == IndexJobKind.Describe)
                    && j.Status == IndexJobStatus.Pending)
                .ExecuteDeleteAsync(cancellationToken);

            queue.Enqueue(job.MachineId, job.Path, kind);
        }

        /// <summary>Postgres text rejects NUL, and the full-text trigger rejects oversized content.</summary>
        private string? CleanContent(string? content) =>
            string.IsNullOrWhiteSpace(content) ? null : Truncate(content.Replace("\0", string.Empty), _options.MaxContentChars);

        /// <summary>Shutdown isn't the file's fault: the job goes back without using up an attempt.</summary>
        private static void ReturnUnfinished(IndexJob job)
        {
            job.Status = IndexJobStatus.Pending;
            job.Attempts = Math.Max(0, job.Attempts - 1);
        }

        private static void Fail(IndexJob job, string error)
        {
            job.Status = IndexJobStatus.Failed;
            job.Error = Truncate(error, 2000);
        }
    }
}
