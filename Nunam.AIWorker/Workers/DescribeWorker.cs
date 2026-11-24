using System.IO;
using Microsoft.Extensions.Options;
using Nunam.Core.AI;
using Nunam.Core.Agents;
using Nunam.Core.Entities;
using Nunam.Core.Files;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Consumes <see cref="IndexJobKind.Describe"/> jobs: feeds each file's preview image to the vision
    /// model and sends back the text for <see cref="IndexedFile.Description"/>. The file's Embed job is
    /// queued with the result, once the description exists (or describing has failed for good), so the
    /// vector always includes what the vision model saw.
    /// </summary>
    public class DescribeWorker(
        WorkerServer server,
        IServiceScopeFactory scopeFactory,
        IOptions<AIWorkerOptions> options,
        ILogger<DescribeWorker> logger) : PollingWorker(logger)
    {
        private readonly AIWorkerOptions _options = options.Value;
        private readonly ILogger<DescribeWorker> _logger = logger;
        private readonly ResultOutbox _outbox = new(server, logger);

        protected override string Name => nameof(DescribeWorker);
        protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(_options.IdlePollSeconds);
        protected override TimeSpan MinBackoff => TimeSpan.FromSeconds(_options.MinBackoffSeconds);
        protected override TimeSpan MaxBackoff => TimeSpan.FromSeconds(_options.MaxBackoffSeconds);

        protected override async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            var (requeued, failed) = await server.RecoverAsync(IndexJobKind.Describe, _options.StaleJobMinutes, _options.MaxAttempts, cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Describe job(s)", requeued);
            if (failed > 0) _logger.LogError("Gave up on {Count} Describe job(s) that stopped the worker {Max} times", failed, _options.MaxAttempts);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            if (!await _outbox.SendAsync(cancellationToken)) return PollResult.Backoff;

            using var scope = scopeFactory.CreateScope();
            var ai = scope.ServiceProvider.GetRequiredService<IAIInferenceService>();

            var jobs = await server.ClaimAsync(IndexJobKind.Describe, _options.DescribeBatchSize, 0, cancellationToken);
            if (jobs.Count == 0)
            {
                return PollResult.NoWork;
            }

            var backendDown = false;

            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                try
                {
                    if (job.PreviewImage is not { Length: > 0 } preview)
                    {
                        _outbox.Add(Result(job, JobOutcome.Failed, "File has no preview image to describe.", queueEmbed: true));
                        continue;
                    }

                    var mediaType = MediaTypes.Detect(preview, job.Extension ?? Path.GetExtension(job.FileName));
                    var description = await ai.DescribeImageAsync(preview, mediaType, cancellationToken: cancellationToken);
                    _outbox.Add(new WorkerJobResult(job.Id, JobOutcome.Done, null, description, null, QueueEmbed: true));
                }
                catch (Exception ex)
                {
                    var failure = Classify(ex, cancellationToken);
                    if (failure is BackendFailure.Outage or BackendFailure.Shutdown)
                    {
                        // Return this job and any not-yet-processed ones to the queue for a later retry.
                        if (failure is BackendFailure.Outage)
                        {
                            _logger.LogWarning(ex, "AI backend unreachable; returning {Count} Describe job(s) to the queue",
                                jobs.Count - i);
                        }
                        for (var j = i; j < jobs.Count; j++)
                        {
                            _outbox.Add(Result(jobs[j], JobOutcome.Returned));
                        }
                        backendDown = failure is BackendFailure.Outage;
                        break;
                    }

                    if (failure is BackendFailure.JobFailed && job.Attempts < _options.MaxAttempts)
                    {
                        _logger.LogWarning(ex, "Describing {Path} failed (attempt {Attempt} of {Max}); will retry",
                            job.Path, job.Attempts, _options.MaxAttempts);
                        _outbox.Add(Result(job, JobOutcome.Retry));
                        continue;
                    }

                    // The model rejected this image, or kept failing on it: embed the file without a description.
                    _logger.LogError(ex, "Describe job {JobId} failed", job.Id);
                    _outbox.Add(Result(job, JobOutcome.Failed, ex.Message, queueEmbed: true));
                }
            }

            // Not cancellable: on shutdown the results (and jobs returned to the queue) must still land,
            // otherwise the claimed jobs sit in Processing until stale recovery picks them up.
            if (!await _outbox.SendAsync(CancellationToken.None)) return PollResult.Backoff;
            return backendDown ? PollResult.Backoff : PollResult.DidWork;
        }

        private static WorkerJobResult Result(WorkerJob job, JobOutcome outcome, string? error = null, bool queueEmbed = false) =>
            new(job.Id, outcome, error is null ? null : Truncate(error, 2000), null, null, queueEmbed);
    }
}
