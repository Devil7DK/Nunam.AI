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
            var requeued = await queue.RequeueStaleAsync(
                IndexJobKind.Embed, TimeSpan.FromMinutes(_options.StaleJobMinutes), cancellationToken: cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Embed job(s)", requeued);
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
                var vectors = await ai.EmbedAsync([.. batch.Select(b => b.Input)], cancellationToken);

                for (var i = 0; i < batch.Count; i++)
                {
                    batch[i].File.Embedding = vectors[i];
                    batch[i].Job.Status = IndexJobStatus.Done;
                    batch[i].Job.Stage = IndexJobStage.Embed;
                    batch[i].Job.Error = null;
                }
            }
            catch (Exception ex) when (IsBackendUnreachable(ex))
            {
                _logger.LogWarning(ex, "AI backend unreachable; returning {Count} Embed job(s) to the queue", batch.Count);
                foreach (var (job, _, _) in batch)
                {
                    job.Status = IndexJobStatus.Pending;
                }
                await db.SaveChangesAsync(CancellationToken.None);
                return PollResult.Backoff;
            }
            catch (Exception ex)
            {
                // A non-connectivity failure on the shared call fails the whole batch.
                _logger.LogError(ex, "Embed batch of {Count} failed", batch.Count);
                foreach (var (job, _, _) in batch)
                {
                    Fail(job, ex.Message);
                }
            }

            await db.SaveChangesAsync(CancellationToken.None);
            return PollResult.DidWork;
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
