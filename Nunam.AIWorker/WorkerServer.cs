using Nunam.Core.Agents;
using Nunam.Core.Entities;
using Nunam.Core.Jobs;

namespace Nunam.AIWorker
{
    /// <summary>The API calls the AI worker makes, signed in with its token.</summary>
    public sealed class WorkerServer(HttpClient http)
    {
        private const string Base = "api/agent/worker/";

        public Task<List<WorkerJob>> ClaimAsync(IndexJobKind kind, int batchSize, int contentCharLimit, CancellationToken ct) =>
            http.SendAsync<List<WorkerJob>>(HttpMethod.Post, Base + "jobs/claim", new WorkerClaimRequest(kind, batchSize, contentCharLimit), ct);

        public Task CompleteAsync(List<WorkerJobResult> results, CancellationToken ct) =>
            http.SendAsync(HttpMethod.Post, Base + "jobs/results", results, ct);

        public Task<StaleRecovery> RecoverAsync(IndexJobKind kind, int staleMinutes, int maxAttempts, CancellationToken ct) =>
            http.SendAsync<StaleRecovery>(HttpMethod.Post, Base + "jobs/recover", new WorkerRecoverRequest(kind, staleMinutes, maxAttempts), ct);
    }
}
