using Nunam.Core.Agents;
using Nunam.Core.Jobs;

namespace Nunam.Indexer.Server
{
    /// <summary>The API calls an indexer makes, signed in with its machine token.</summary>
    public sealed class IndexerServer(HttpClient http)
    {
        private const string Base = "api/agent/indexer/";

        public Task<IndexerHelloResponse> HelloAsync(IndexerHelloRequest request, CancellationToken ct) =>
            http.SendAsync<IndexerHelloResponse>(HttpMethod.Post, Base + "hello", request, ct);

        public Task<IndexerSettingsResponse> GetSettingsAsync(CancellationToken ct) =>
            http.SendAsync<IndexerSettingsResponse>(HttpMethod.Get, Base + "settings", null, ct);

        public Task<IndexerPollResponse> PollAsync(CancellationToken ct) =>
            http.SendAsync<IndexerPollResponse>(HttpMethod.Get, Base + "poll", null, ct);

        public Task ReportStatusAsync(IndexerStatusReport report, CancellationToken ct) =>
            http.SendAsync(HttpMethod.Put, Base + "status", report, ct);

        public Task CompleteCommandAsync(Guid id, CommandResult result, CancellationToken ct) =>
            http.SendAsync(HttpMethod.Post, $"{Base}commands/{id}", result, ct);

        public Task StoreLogsAsync(List<LogRecord> records, CancellationToken ct) =>
            http.SendAsync(HttpMethod.Post, Base + "logs", records, ct);

        public async Task<int> ScanAsync(List<ObservedFile> files, CancellationToken ct) =>
            (await http.SendAsync<CountResponse>(HttpMethod.Post, Base + "scan", new ScanBatchRequest(files), ct)).Count;

        public Task<IndexedPathPage> ListPathsAsync(string? after, int limit, CancellationToken ct) =>
            http.SendAsync<IndexedPathPage>(HttpMethod.Get,
                $"{Base}files?limit={limit}{(after is null ? "" : "&after=" + Uri.EscapeDataString(after))}", null, ct);

        public async Task<int> RemoveAsync(List<string> paths, CancellationToken ct) =>
            (await http.SendAsync<CountResponse>(HttpMethod.Post, Base + "files/remove",
                new RemoveFilesRequest(paths, Path.DirectorySeparatorChar), ct)).Count;

        public async Task<int> EnqueueParseAsync(List<string> paths, CancellationToken ct) =>
            (await http.SendAsync<CountResponse>(HttpMethod.Post, Base + "parse-jobs", new EnqueueParseRequest(paths), ct)).Count;

        public Task<List<ClaimedParseJob>> ClaimParseJobsAsync(int batchSize, CancellationToken ct) =>
            http.SendAsync<List<ClaimedParseJob>>(HttpMethod.Post, Base + "parse-jobs/claim", new ClaimRequest(batchSize), ct);

        public Task<StaleRecovery> RecoverParseJobsAsync(int staleMinutes, int maxAttempts, CancellationToken ct) =>
            http.SendAsync<StaleRecovery>(HttpMethod.Post, Base + "parse-jobs/recover", new RecoverRequest(staleMinutes, maxAttempts), ct);

        public async Task<int> RetryFailedAsync(CancellationToken ct) =>
            (await http.SendAsync<CountResponse>(HttpMethod.Post, Base + "parse-jobs/retry-failed", null, ct)).Count;

        public Task CompleteParseJobAsync(Guid id, ParseResult result, CancellationToken ct) =>
            http.SendAsync(HttpMethod.Post, $"{Base}parse-jobs/{id}/result", result, ct);
    }
}
