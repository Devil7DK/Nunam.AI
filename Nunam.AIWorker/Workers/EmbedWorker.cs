using Microsoft.Extensions.Options;
using Nunam.Core.AI;
using Nunam.Core.Agents;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Consumes <see cref="IndexJobKind.Embed"/> jobs in batches: builds an embedding input from
    /// each file's name + description + a slice of its content, embeds the whole batch in one call,
    /// and sends the vectors back for <see cref="IndexedFile.Embedding"/>.
    /// </summary>
    public class EmbedWorker(
        WorkerServer server,
        IServiceScopeFactory scopeFactory,
        IOptions<AIWorkerOptions> options,
        ILogger<EmbedWorker> logger) : PollingWorker(logger)
    {
        private readonly AIWorkerOptions _options = options.Value;
        private readonly ILogger<EmbedWorker> _logger = logger;
        private readonly ResultOutbox _outbox = new(server, logger);

        protected override string Name => nameof(EmbedWorker);
        protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(_options.IdlePollSeconds);
        protected override TimeSpan MinBackoff => TimeSpan.FromSeconds(_options.MinBackoffSeconds);
        protected override TimeSpan MaxBackoff => TimeSpan.FromSeconds(_options.MaxBackoffSeconds);

        protected override async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            var (requeued, failed) = await server.RecoverAsync(IndexJobKind.Embed, _options.StaleJobMinutes, _options.MaxAttempts, cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Embed job(s)", requeued);
            if (failed > 0) _logger.LogError("Gave up on {Count} Embed job(s) that stopped the worker {Max} times", failed, _options.MaxAttempts);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            if (!await _outbox.SendAsync(cancellationToken)) return PollResult.Backoff;

            using var scope = scopeFactory.CreateScope();
            var ai = scope.ServiceProvider.GetRequiredService<IAIInferenceService>();

            var jobs = await server.ClaimAsync(IndexJobKind.Embed, _options.EmbedBatchSize, _options.ContentCharLimit, cancellationToken);
            if (jobs.Count == 0)
            {
                return PollResult.NoWork;
            }

            var result = PollResult.DidWork;
            try
            {
                var vectors = await ai.EmbedDocumentsAsync([.. jobs.Select(BuildInput)], cancellationToken);
                for (var i = 0; i < jobs.Count; i++)
                {
                    _outbox.Add(new WorkerJobResult(jobs[i].Id, JobOutcome.Done, null, null, vectors[i].ToArray(), false));
                }
            }
            catch (Exception ex)
            {
                var failure = Classify(ex, cancellationToken);
                if (failure is BackendFailure.Outage or BackendFailure.Shutdown)
                {
                    if (failure is BackendFailure.Outage)
                    {
                        _logger.LogWarning(ex, "AI backend unreachable; returning {Count} Embed job(s) to the queue", jobs.Count);
                        result = PollResult.Backoff;
                    }
                    foreach (var job in jobs) _outbox.Add(Result(job, JobOutcome.Returned));
                }
                else
                {
                    // One input can sink the shared call; embed one at a time so only that file's job fails.
                    _logger.LogWarning(ex, "Embed batch of {Count} failed; retrying each file on its own", jobs.Count);
                    if (!await EmbedOneByOneAsync(ai, jobs, cancellationToken)) result = PollResult.Backoff;
                }
            }

            // Not cancellable: the vectors cost GPU time and must land even when shutdown starts.
            return await _outbox.SendAsync(CancellationToken.None) ? result : PollResult.Backoff;
        }

        /// <summary>Returns false when the backend went away part-way; unfinished jobs go back to the queue.</summary>
        private async Task<bool> EmbedOneByOneAsync(IAIInferenceService ai, List<WorkerJob> jobs, CancellationToken cancellationToken)
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                try
                {
                    var vector = (await ai.EmbedDocumentsAsync([BuildInput(job)], cancellationToken))[0];
                    _outbox.Add(new WorkerJobResult(job.Id, JobOutcome.Done, null, null, vector.ToArray(), false));
                }
                catch (Exception ex)
                {
                    var failure = Classify(ex, cancellationToken);
                    if (failure is BackendFailure.Outage or BackendFailure.Shutdown)
                    {
                        for (var j = i; j < jobs.Count; j++) _outbox.Add(Result(jobs[j], JobOutcome.Returned));
                        return failure is BackendFailure.Shutdown;
                    }

                    if (failure is BackendFailure.JobFailed && job.Attempts < _options.MaxAttempts)
                    {
                        _logger.LogWarning(ex, "Embedding {Path} failed (attempt {Attempt} of {Max}); will retry",
                            job.Path, job.Attempts, _options.MaxAttempts);
                        _outbox.Add(Result(job, JobOutcome.Retry));
                        continue;
                    }

                    _logger.LogError(ex, "Embed job {JobId} for {Path} failed", job.Id, job.Path);
                    _outbox.Add(Result(job, JobOutcome.Failed, ex.Message));
                }
            }

            return true;
        }

        private string BuildInput(WorkerJob job)
        {
            var parts = new List<string>(3) { job.FileName };

            if (!string.IsNullOrWhiteSpace(job.Description))
            {
                parts.Add(job.Description);
            }

            if (!string.IsNullOrWhiteSpace(job.Content))
            {
                parts.Add(Truncate(job.Content, _options.ContentCharLimit));
            }

            return string.Join('\n', parts);
        }

        private static WorkerJobResult Result(WorkerJob job, JobOutcome outcome, string? error = null) =>
            new(job.Id, outcome, error is null ? null : Truncate(error, 2000), null, null, false);
    }
}
