using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nunam.API.Auth;
using Nunam.API.Services;
using Nunam.Core.Agents;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;
using Pgvector;

namespace Nunam.API.Endpoints
{
    /// <summary>The AI worker's queue: Describe and Embed jobs for every machine.</summary>
    public static class AgentWorkerEndpoints
    {
        public static IEndpointRouteBuilder MapAgentWorkerEndpoints(this IEndpointRouteBuilder app)
        {
            var worker = app.MapGroup("/api/agent/worker").RequireAuthorization(Policies.AIWorker);
            worker.MapPost("/jobs/claim", ClaimAsync);
            worker.MapPost("/jobs/results", CompleteAsync);
            worker.MapPost("/jobs/recover", RecoverAsync);
            worker.MapGet("/describe-providers", DescribeProvidersAsync);
            worker.MapPost("/describe-providers/{id:guid}/status", ProviderStatusAsync);
            return app;
        }

        private static bool IsWorkerKind(IndexJobKind kind) => kind is IndexJobKind.Describe or IndexJobKind.Embed;

        /// <summary>
        /// Claims jobs with what the worker needs from each file: the preview to describe, or the text to
        /// embed. A job whose file is gone fails here.
        /// </summary>
        private static async Task<Results<Ok<List<WorkerJob>>, ValidationProblem>> ClaimAsync(
            WorkerClaimRequest request, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (!IsWorkerKind(request.Kind)) return KindProblem();

            var describe = request.Kind == IndexJobKind.Describe;
            var contentLimit = Math.Clamp(request.ContentCharLimit, 0, 100_000);
            var jobs = await new JobQueue(db).ClaimAsync(request.Kind, Math.Clamp(request.BatchSize, 1, 256), cancellationToken: cancellationToken);

            var claimed = new List<WorkerJob>(jobs.Count);
            var missing = new List<Guid>();
            foreach (var job in jobs)
            {
                var file = await db.Files
                    .Where(f => f.MachineId == job.MachineId && f.Path == job.Path)
                    .Select(f => new
                    {
                        f.FileName,
                        f.Extension,
                        f.Description,
                        Content = describe || f.Content == null ? null : f.Content.Substring(0, contentLimit),
                        PreviewImage = describe ? f.PreviewImage : null,
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                if (file is null)
                {
                    missing.Add(job.Id);
                    continue;
                }
                claimed.Add(new WorkerJob(job.Id, job.Attempts, job.Path, file.FileName, file.Extension,
                    file.Description, file.Content, file.PreviewImage));
            }

            if (missing.Count > 0)
            {
                await db.IndexJobs.Where(j => missing.Contains(j.Id))
                    .ExecuteUpdateAsync(j => j
                        .SetProperty(x => x.Status, IndexJobStatus.Failed)
                        .SetProperty(x => x.Error, "No matching IndexedFile for this job.")
                        .SetProperty(x => x.LastModified, DateTimeOffset.UtcNow), CancellationToken.None);
            }
            return TypedResults.Ok(claimed);
        }

        /// <summary>
        /// Stores what the worker produced and settles its jobs. Results for jobs that are no longer
        /// <c>Processing</c> (stale recovery took them back) are dropped.
        /// </summary>
        private static async Task<Results<NoContent, ValidationProblem>> CompleteAsync(
            List<WorkerJobResult> results, NunamDbContext db, IOptions<AIOptions> ai, CancellationToken cancellationToken)
        {
            var providerIds = results
                .SelectMany(r => (r.EmptyAttempts ?? []).Append(r.Usage))
                .OfType<DescribeUsageReport>()
                .Select(u => u.ProviderId).Distinct().ToList();
            var providers = await db.DescribeProviders.AsTracking()
                .Where(p => providerIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var ids = results.Select(r => r.Id).ToList();
            var jobs = await db.IndexJobs.AsTracking()
                .Where(j => ids.Contains(j.Id) && j.Status == IndexJobStatus.Processing
                    && (j.Kind == IndexJobKind.Describe || j.Kind == IndexJobKind.Embed))
                .ToDictionaryAsync(j => j.Id, cancellationToken);
            var queue = new JobQueue(db);
            var dimensions = ai.Value.EmbeddingDimensions;

            foreach (var result in results)
            {
                if (!jobs.TryGetValue(result.Id, out var job)) continue;

                switch (result.Outcome)
                {
                    case JobOutcome.Done:
                        var file = await db.Files.AsTracking()
                            .FirstOrDefaultAsync(f => f.MachineId == job.MachineId && f.Path == job.Path, cancellationToken);
                        if (file is null)
                        {
                            Fail(job, "No matching IndexedFile for this job.");
                            break;
                        }
                        if (job.Kind == IndexJobKind.Describe)
                        {
                            var provider = result.Usage is { } used ? providers.GetValueOrDefault(used.ProviderId) : null;
                            file.Description = result.Description;
                            file.DescriptionModel = provider?.Model;
                            file.DescriptionProvider = provider?.Name;
                            file.DescribedAt = DateTimeOffset.UtcNow;
                        }
                        else if (result.Embedding is { } vector && vector.Length == dimensions)
                        {
                            file.Embedding = new Vector(vector);
                        }
                        else
                        {
                            Fail(job, $"The embedding has {result.Embedding?.Length ?? 0} dimensions; the index holds {dimensions}.");
                            break;
                        }
                        job.Status = IndexJobStatus.Done;
                        job.Stage = job.Kind == IndexJobKind.Describe ? IndexJobStage.Describe : IndexJobStage.Embed;
                        job.Error = null;
                        break;
                    case JobOutcome.Failed:
                        Fail(job, result.Error ?? "Failed.");
                        break;
                    case JobOutcome.Retry:
                        job.Status = IndexJobStatus.Pending;
                        break;
                    case JobOutcome.Returned:
                        job.ReturnToQueue();
                        break;
                }

                if (result.QueueEmbed && job.Kind == IndexJobKind.Describe) queue.Enqueue(job.MachineId, job.Path, IndexJobKind.Embed);
            }

            foreach (var result in results)
            {
                foreach (var attempt in result.EmptyAttempts ?? [])
                {
                    if (providers.TryGetValue(attempt.ProviderId, out var gaveNothing))
                        await DescribeProviders.RecordAsync(db, gaveNothing, attempt.InputTokens, attempt.OutputTokens, true, CancellationToken.None);
                }
                if (result.Usage is not { } usage || !providers.TryGetValue(usage.ProviderId, out var provider)) continue;
                var failed = result.Outcome != JobOutcome.Done;
                await DescribeProviders.RecordAsync(db, provider, usage.InputTokens, usage.OutputTokens, failed, CancellationToken.None);
                if (!failed) provider.LastUsedAt = DateTimeOffset.UtcNow;
            }

            // Not cancellable: the GPU time is spent; the results must land.
            await db.SaveChangesAsync(CancellationToken.None);
            return TypedResults.NoContent();
        }

        /// <summary>
        /// The providers the worker may use now, with their API keys: enabled, and within budget. A key
        /// this server can't decrypt leaves its provider out.
        /// </summary>
        private static async Task<List<WorkerDescribeProvider>> DescribeProvidersAsync(
            NunamDbContext db, SecretProtector secrets, ILogger<WorkerDescribeProvider> logger, CancellationToken cancellationToken)
        {
            var spend = await DescribeProviders.SpendAsync(db, cancellationToken);
            var providers = await db.DescribeProviders.Where(p => p.Enabled).ToListAsync(cancellationToken);
            var usable = new List<WorkerDescribeProvider>(providers.Count);
            foreach (var p in providers)
            {
                var remaining = DescribeProviders.Remaining(p, spend.GetValueOrDefault(p.Id));
                if (remaining is 0) continue;

                string? apiKey = null;
                if (p.ApiKeyProtected is { } stored && (apiKey = secrets.Unprotect(stored)) is null)
                {
                    logger.LogWarning("The API key of describe provider {Name} can't be decrypted with this server's secrets key", p.Name);
                    continue;
                }
                usable.Add(new WorkerDescribeProvider(p.Id, p.Name, p.Kind, p.BaseUrl, apiKey, p.Model, p.Prompt,
                    p.MaxOutputTokens, p.DisableReasoning, p.Priority, p.Concurrency, p.TakesOverflow,
                    p.InputPricePerMillion, p.OutputPricePerMillion, remaining));
            }
            return usable;
        }

        private static async Task<Results<NoContent, NotFound>> ProviderStatusAsync(
            Guid id, DescribeProviderStatus status, NunamDbContext db, CancellationToken cancellationToken)
        {
            var error = status.Error is { Length: > 1000 } e ? e[..1000] : status.Error;
            var updated = await db.DescribeProviders.Where(p => p.Id == id)
                .ExecuteUpdateAsync(p => p
                    .SetProperty(x => x.LastError, error)
                    .SetProperty(x => x.LastErrorAt, x => error == null ? x.LastErrorAt : DateTimeOffset.UtcNow), cancellationToken);
            return updated == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        }

        private static async Task<Results<Ok<StaleRecovery>, ValidationProblem>> RecoverAsync(
            WorkerRecoverRequest request, NunamDbContext db, CancellationToken cancellationToken)
        {
            if (!IsWorkerKind(request.Kind)) return KindProblem();
            return TypedResults.Ok(await new JobQueue(db).RecoverStaleAsync(
                request.Kind, TimeSpan.FromMinutes(Math.Max(1, request.StaleMinutes)), Math.Max(1, request.MaxAttempts),
                cancellationToken: cancellationToken));
        }

        private static void Fail(IndexJob job, string error)
        {
            job.Status = IndexJobStatus.Failed;
            job.Error = error.Length <= 2000 ? error : error[..2000];
        }

        private static ValidationProblem KindProblem() =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["Use describe or embed."] });
    }
}
