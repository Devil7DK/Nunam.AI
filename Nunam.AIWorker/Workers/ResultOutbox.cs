using System.Net;
using Nunam.Core.Agents;

namespace Nunam.AIWorker.Workers
{
    /// <summary>
    /// Results the server hasn't taken yet. A worker sends them before claiming more, so GPU time spent
    /// while the server was briefly away isn't thrown away.
    /// </summary>
    public sealed class ResultOutbox(WorkerServer server, ILogger logger)
    {
        private readonly List<WorkerJobResult> _results = [];

        public void Add(WorkerJobResult result) => _results.Add(result);

        /// <summary>Returns false while the server can't be reached; the results are kept for the next try.</summary>
        public async Task<bool> SendAsync(CancellationToken cancellationToken)
        {
            if (_results.Count == 0) return true;
            try
            {
                await server.CompleteAsync(_results, cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
            {
                // Stale recovery gives the jobs back to the queue.
                logger.LogError(ex, "The server refused {Count} result(s); dropping them", _results.Count);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not send {Count} result(s) to the server; will retry", _results.Count);
                return false;
            }
            _results.Clear();
            return true;
        }
    }
}
