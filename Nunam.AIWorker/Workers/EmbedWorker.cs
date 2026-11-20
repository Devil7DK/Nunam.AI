using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Consumes <see cref="IndexJobKind.Embed"/> jobs in batches: builds an embedding input from
    /// each file's name + description + a slice of its content, embeds the whole batch in one call,
    /// and stores the vectors in <see cref="IndexedFile.Embedding"/>.
    /// </summary>
    public class EmbedWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AIWorkerOptions> options,
        ILogger<EmbedWorker> logger) : PollingWorker(logger)
    {
        private readonly AIWorkerOptions _options = options.Value;
        private readonly ILogger<EmbedWorker> _logger = logger;

        protected override string Name => nameof(EmbedWorker);
        protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(_options.IdlePollSeconds);
        protected override TimeSpan MinBackoff => TimeSpan.FromSeconds(_options.MinBackoffSeconds);
        protected override TimeSpan MaxBackoff => TimeSpan.FromSeconds(_options.MaxBackoffSeconds);

        protected override async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var (requeued, failed) = await queue.RecoverStaleAsync(
                IndexJobKind.Embed, TimeSpan.FromMinutes(_options.StaleJobMinutes), _options.MaxAttempts, cancellationToken: cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Embed job(s)", requeued);
            if (failed > 0) _logger.LogError("Gave up on {Count} Embed job(s) that stopped the worker {Max} times", failed, _options.MaxAttempts);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var ai = scope.ServiceProvider.GetRequiredService<IAIInferenceService>();

            var jobs = await queue.ClaimAsync(IndexJobKind.Embed, _options.EmbedBatchSize, cancellationToken: cancellationToken);
            if (jobs.Count == 0)
            {
                return PollResult.NoWork;
            }

            // Resolve each job to its file and build the embedding input. Jobs with no file fail individually.
            var batch = new List<(IndexJob Job, IndexedFile File, string Input)>(jobs.Count);
            foreach (var job in jobs)
            {
                var file = await db.Files.FirstOrDefaultAsync(
                    f => f.MachineId == job.MachineId && f.Path == job.Path, cancellationToken);

                if (file is null)
                {
                    Fail(job, "No matching IndexedFile for this job.");
                    continue;
                }

                batch.Add((job, file, BuildInput(file)));
            }

            if (batch.Count == 0)
            {
                // Only missing-file failures this round — still progress, save and move on.
                await db.SaveChangesAsync(CancellationToken.None);
                return PollResult.DidWork;
            }

            try
            {
                var vectors = await ai.EmbedDocumentsAsync([.. batch.Select(b => b.Input)], cancellationToken);

                for (var i = 0; i < batch.Count; i++)
                {
                    batch[i].File.Embedding = vectors[i];
                    batch[i].Job.Status = IndexJobStatus.Done;
                    batch[i].Job.Stage = IndexJobStage.Embed;
                    batch[i].Job.Error = null;
                }
            }
            catch (Exception ex)
            {
                var failure = Classify(ex, cancellationToken);
                if (failure is BackendFailure.Outage or BackendFailure.Shutdown)
                {
                    if (failure is BackendFailure.Outage)
                    {
                        _logger.LogWarning(ex, "AI backend unreachable; returning {Count} Embed job(s) to the queue", batch.Count);
                    }
                    foreach (var (job, _, _) in batch)
                    {
                        job.ReturnToQueue();
                    }
                    await db.SaveChangesAsync(CancellationToken.None);
                    return failure is BackendFailure.Outage ? PollResult.Backoff : PollResult.DidWork;
                }

                // One input can sink the shared call; embed one at a time so only that file's job fails.
                _logger.LogWarning(ex, "Embed batch of {Count} failed; retrying each file on its own", batch.Count);
                if (!await EmbedOneByOneAsync(ai, batch, cancellationToken))
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                    return PollResult.Backoff;
                }
            }

            await db.SaveChangesAsync(CancellationToken.None);
            return PollResult.DidWork;
        }

        /// <summary>Returns false when the backend went away part-way; unfinished jobs are back in the queue.</summary>
        private async Task<bool> EmbedOneByOneAsync(
            IAIInferenceService ai, List<(IndexJob Job, IndexedFile File, string Input)> batch, CancellationToken cancellationToken)
        {
            for (var i = 0; i < batch.Count; i++)
            {
                var (job, file, input) = batch[i];
                try
                {
                    file.Embedding = (await ai.EmbedDocumentsAsync([input], cancellationToken))[0];
                    job.Status = IndexJobStatus.Done;
                    job.Stage = IndexJobStage.Embed;
                    job.Error = null;
                }
                catch (Exception ex)
                {
                    var failure = Classify(ex, cancellationToken);
                    if (failure is BackendFailure.Outage or BackendFailure.Shutdown)
                    {
                        for (var j = i; j < batch.Count; j++)
                        {
                            batch[j].Job.ReturnToQueue();
                        }
                        return failure is BackendFailure.Shutdown;
                    }

                    if (failure is BackendFailure.JobFailed && job.RetryOrFail(_options.MaxAttempts, ex.Message))
                    {
                        _logger.LogWarning(ex, "Embedding {Path} failed (attempt {Attempt} of {Max}); will retry",
                            job.Path, job.Attempts, _options.MaxAttempts);
                        continue;
                    }

                    _logger.LogError(ex, "Embed job {JobId} for {Path} failed", job.Id, job.Path);
                    Fail(job, ex.Message);
                }
            }

            return true;
        }

        private string BuildInput(IndexedFile file)
        {
            var parts = new List<string>(3) { file.FileName };

            if (!string.IsNullOrWhiteSpace(file.Description))
            {
                parts.Add(file.Description);
            }

            if (!string.IsNullOrWhiteSpace(file.Content))
            {
                parts.Add(Truncate(file.Content, _options.ContentCharLimit));
            }

            return string.Join('\n', parts);
        }

        private static void Fail(IndexJob job, string error)
        {
            job.Status = IndexJobStatus.Failed;
            job.Error = Truncate(error, 2000);
        }
    }
}
