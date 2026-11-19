using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Files;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Consumes <see cref="IndexJobKind.Describe"/> jobs: feeds each file's preview image
    /// to the vision model and stores the resulting text in <see cref="IndexedFile.Description"/>.
    /// The file's Embed job is enqueued here, once the description exists (or describing has
    /// failed for good), so the vector always includes what the vision model saw.
    /// </summary>
    public class DescribeWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AIWorkerOptions> options,
        ILogger<DescribeWorker> logger) : PollingWorker(logger)
    {
        private readonly AIWorkerOptions _options = options.Value;
        private readonly ILogger<DescribeWorker> _logger = logger;

        protected override string Name => nameof(DescribeWorker);
        protected override TimeSpan IdleInterval => TimeSpan.FromSeconds(_options.IdlePollSeconds);
        protected override TimeSpan MinBackoff => TimeSpan.FromSeconds(_options.MinBackoffSeconds);
        protected override TimeSpan MaxBackoff => TimeSpan.FromSeconds(_options.MaxBackoffSeconds);

        protected override async Task RecoverStaleAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var requeued = await queue.RequeueStaleAsync(
                IndexJobKind.Describe, TimeSpan.FromMinutes(_options.StaleJobMinutes), cancellationToken: cancellationToken);
            if (requeued > 0) _logger.LogWarning("Requeued {Count} stale Describe job(s)", requeued);
        }

        protected override async Task<PollResult> PollAsync(CancellationToken cancellationToken)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NunamDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<JobQueue>();
            var ai = scope.ServiceProvider.GetRequiredService<IAIInferenceService>();

            var jobs = await queue.ClaimAsync(IndexJobKind.Describe, _options.DescribeBatchSize, cancellationToken: cancellationToken);
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
                    var file = await db.Files.FirstOrDefaultAsync(
                        f => f.MachineId == job.MachineId && f.Path == job.Path, cancellationToken);

                    if (file is null)
                    {
                        Fail(job, "No matching IndexedFile for this job.");
                        continue;
                    }

                    if (file.PreviewImage is null || file.PreviewImage.Length == 0)
                    {
                        Fail(job, "File has no preview image to describe.");
                        queue.Enqueue(job.MachineId, job.Path, IndexJobKind.Embed);
                        continue;
                    }

                    var mediaType = MediaTypes.FromExtension(
                        file.Extension ?? Path.GetExtension(file.FileName));

                    file.Description = await ai.DescribeImageAsync(
                        file.PreviewImage, mediaType, cancellationToken: cancellationToken);

                    job.Status = IndexJobStatus.Done;
                    job.Stage = IndexJobStage.Describe;
                    job.Error = null;
                    queue.Enqueue(job.MachineId, job.Path, IndexJobKind.Embed);
                }
                catch (Exception ex) when (IsBackendUnreachable(ex))
                {
                    // Return this job and any not-yet-processed ones to the queue for a later retry.
                    _logger.LogWarning(ex, "AI backend unreachable; returning {Count} Describe job(s) to the queue",
                        jobs.Count - i);
                    for (var j = i; j < jobs.Count; j++)
                    {
                        jobs[j].Status = IndexJobStatus.Pending;
                    }
                    backendDown = true;
                    break; // returns PollResult.Backoff below
                }
                catch (Exception ex)
                {
                    // The model rejected this image; embed the file without a description.
                    _logger.LogError(ex, "Describe job {JobId} failed", job.Id);
                    Fail(job, ex.Message);
                    queue.Enqueue(job.MachineId, job.Path, IndexJobKind.Embed);
                }
            }

            // Not cancellable: on shutdown the results (and jobs returned to Pending) must still land,
            // otherwise the claimed rows sit in Processing until stale recovery picks them up.
            await db.SaveChangesAsync(CancellationToken.None);
            return backendDown ? PollResult.Backoff : PollResult.DidWork;
        }

        private static void Fail(IndexJob job, string error)
        {
            job.Status = IndexJobStatus.Failed;
            job.Error = Truncate(error, 2000);
        }
    }
}
