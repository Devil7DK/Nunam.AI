using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nunam.Core.Agents;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;
using Nunam.Indexer.Abstractions;
using Nunam.Indexer.Indexing;
using Nunam.Indexer.Management;
using Nunam.Indexer.Parsing;
using Nunam.Indexer.Providers;
using Nunam.Indexer.Server;

namespace Nunam.Indexer.Workers
{
    /// <summary>
    /// Consumes this machine's <see cref="IndexJobKind.Parse"/> jobs: detects the file type, runs the
    /// parsers configured for it until one reads it, and sends the result to the server, which stores
    /// the file and queues the AI job its new content needs.
    /// </summary>
    public class ParseWorker(
        IndexerServer server,
        FileTypeDetector detector,
        ProviderRegistry providers,
        IndexScope scope,
        IndexerControl control,
        IOptions<IndexerOptions> options,
        ILogger<ParseWorker> logger) : PollingWorker(logger)
    {
        private readonly IndexerOptions _options = options.Value;
        private readonly ILogger<ParseWorker> _logger = logger;

        /// <summary>Results the server didn't take yet; sent before anything new is claimed.</summary>
        private readonly Queue<(Guid JobId, string Path, ParseResult Result)> _unsent = new();

        // Media and document metadata can hold infinite or NaN numbers (a zero frame rate, a division by
        // zero in a header), which plain JSON can't express; they go as strings rather than failing the file.
        private static readonly JsonSerializerOptions MetadataOptions = new()
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        protected override string Name => nameof(ParseWorker);
        protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(_options.IdlePollSeconds);
        protected override TimeSpan MinBackoff => TimeSpan.FromSeconds(_options.MinBackoffSeconds);
        protected override TimeSpan MaxBackoff => TimeSpan.FromSeconds(_options.MaxBackoffSeconds);

        protected override async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            var (requeued, failed) = await server.RecoverParseJobsAsync(_options.StaleJobMinutes, _options.MaxAttempts, cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Parse job(s)", requeued);
            if (failed > 0) _logger.LogError("Gave up on {Count} Parse job(s) that stopped the worker {Max} times", failed, _options.MaxAttempts);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            if (!await SendUnsentAsync(cancellationToken)) return PollResult.Backoff;
            if (control.Paused) return PollResult.NoWork;

            var jobs = await server.ClaimParseJobsAsync(_options.ParseBatchSize, cancellationToken);
            if (jobs.Count == 0)
            {
                return PollResult.NoWork;
            }

            foreach (var job in jobs)
            {
                ParseResult result;
                if (cancellationToken.IsCancellationRequested)
                {
                    result = Returned();
                }
                else
                {
                    var stage = new StageTracker();
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    deadline.CancelAfter(TimeSpan.FromSeconds(_options.ParseTimeoutSeconds));
                    try
                    {
                        result = await ProcessAsync(job, stage, deadline.Token);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        result = Returned();
                    }
                    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                    {
                        _logger.LogWarning("Parsing {Path} took longer than {Seconds}s; giving up on it", job.Path, _options.ParseTimeoutSeconds);
                        result = Failed(stage.Stage, $"Parsing took longer than {_options.ParseTimeoutSeconds} seconds.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Parse job {JobId} for {Path} failed", job.Id, job.Path);
                        result = Failed(stage.Stage, ex.Message);
                    }
                }
                _unsent.Enqueue((job.Id, job.Path, result));
                // Sent as each file finishes: results held until the end of the batch could make the
                // server take the whole batch back as stale behind one slow file.
                await SendUnsentAsync(CancellationToken.None);
            }

            // Not cancellable: finished parses must land even when shutdown starts mid-batch.
            return await SendUnsentAsync(CancellationToken.None) ? PollResult.DidWork : PollResult.Backoff;
        }

        /// <summary>Returns false while the server can't be reached; what's left is kept for the next poll.</summary>
        private async Task<bool> SendUnsentAsync(CancellationToken cancellationToken)
        {
            while (_unsent.TryPeek(out var item))
            {
                try
                {
                    await server.CompleteParseJobAsync(item.JobId, item.Result, cancellationToken);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict)
                {
                    _logger.LogInformation("The server no longer holds the job for {Path} for this indexer; dropping its result", item.Path);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge
                    && item.Result.Outcome == JobOutcome.Done)
                {
                    _logger.LogError(ex, "The server refused the parse result for {Path}", item.Path);
                    _unsent.Dequeue();
                    _unsent.Enqueue((item.JobId, item.Path, Failed(IndexJobStage.Upsert, "The server refused the result: " + ex.Message)));
                    continue;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not send {Count} parse result(s) to the server; will retry", _unsent.Count);
                    return false;
                }
                _unsent.Dequeue();
            }
            return true;
        }

        private async Task<ParseResult> ProcessAsync(ClaimedParseJob job, StageTracker stage, CancellationToken cancellationToken)
        {
            stage.Stage = IndexJobStage.Detect;

            if (!scope.Allows(job.Path, out var reason))
            {
                return Failed(stage.Stage, reason);
            }

            var info = new FileInfo(job.Path);
            if (!info.Exists)
            {
                return Failed(stage.Stage, "File no longer exists.");
            }

            var fileType = detector.Detect(info);

            stage.Stage = IndexJobStage.Parse;
            var (outcome, parserName) = await ParseAsync(providers, info, (FileKind)fileType, cancellationToken);

            stage.Stage = IndexJobStage.Upsert;
            var content = CleanContent(outcome.Content);
            var file = new ParsedFile(
                info.Name, info.Extension, info.Length, fileType, info.CreationTimeUtc, info.LastWriteTimeUtc,
                content,
                outcome.Metadata is { Count: > 0 } metadata ? JsonSerializer.Serialize(metadata, MetadataOptions) : null,
                outcome.PreviewImage,
                outcome.NeedsDescribe);

            _logger.LogInformation(
                "Parsed {Path} with {Parser} (type={Type}, content={ContentLen} chars, preview={HasPreview})",
                job.Path, parserName, fileType, outcome.Content?.Length ?? 0, outcome.PreviewImage is not null);
            return new ParseResult(JobOutcome.Done, stage.Stage, null, file);
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

        /// <summary>Postgres text rejects NUL, and the full-text trigger rejects oversized content.</summary>
        private string? CleanContent(string? content) =>
            string.IsNullOrWhiteSpace(content) ? null : Truncate(content.Replace("\0", string.Empty), _options.MaxContentChars);

        /// <summary>Shutdown isn't the file's fault: the job goes back without using up an attempt.</summary>
        private static ParseResult Returned() => new(JobOutcome.Returned, IndexJobStage.None, null, null);

        private static ParseResult Failed(IndexJobStage stage, string error) =>
            new(JobOutcome.Failed, stage, Truncate(error, 2000), null);

        /// <summary>How far a parse got, for the job's record when it fails.</summary>
        private sealed class StageTracker
        {
            public IndexJobStage Stage { get; set; } = IndexJobStage.None;
        }
    }
}
