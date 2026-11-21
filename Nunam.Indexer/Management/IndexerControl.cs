namespace Nunam.Indexer.Management
{
    /// <summary>
    /// State that outlives a restart of the workers, shared between the supervisor, the workers it
    /// starts and the log shipper.
    /// </summary>
    public class IndexerControl
    {
        private readonly Lock _gate = new();
        private TaskCompletionSource<string> _restart = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset ProcessStartedAt { get; } = DateTimeOffset.UtcNow;
        public DateTimeOffset StartedAt { get; private set; }

        /// <summary>Set once the machine is registered; log entries wait for it.</summary>
        public Guid? MachineId { get; set; }

        /// <summary>Parsing waits while set. Kept across restarts of the workers; restarting the service resumes.</summary>
        public volatile bool Paused;

        /// <summary>The remote settings the running workers use, and their revision.</summary>
        public Nunam.Core.Management.RemoteIndexerSettings? Remote { get; private set; }
        public int? AppliedRevision { get; private set; }

        public DateTimeOffset? ScanStartedAt { get; set; }
        public DateTimeOffset? ScanFinishedAt { get; set; }

        public void BeginRun(Nunam.Core.Management.RemoteIndexerSettings? remote, int? revision)
        {
            lock (_gate)
            {
                _restart = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Remote = remote;
                AppliedRevision = revision;
                StartedAt = DateTimeOffset.UtcNow;
                ScanStartedAt = null;
                ScanFinishedAt = null;
            }
        }

        /// <summary>Asks the supervisor to stop the workers and start them again; the first reason wins.</summary>
        public void RequestRestart(string reason)
        {
            lock (_gate) _restart.TrySetResult(reason);
        }

        public Task<string> WaitForRestartAsync(CancellationToken cancellationToken)
        {
            Task<string> restart;
            lock (_gate) restart = _restart.Task;
            return restart.WaitAsync(cancellationToken);
        }
    }
}
