namespace Nunam.Core.Entities
{
    /// <summary>An action requested from the UI for one machine's indexer, which carries it out and records the outcome.</summary>
    public class IndexerCommand : EntityBase
    {
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        public IndexerCommandKind Kind { get; set; }
        public IndexerCommandStatus Status { get; set; } = IndexerCommandStatus.Pending;
        public string RequestedBy { get; set; } = default!;
        public DateTimeOffset? CompletedAt { get; set; }
        public string? Result { get; set; }
    }

    public enum IndexerCommandKind
    {
        /// <summary>Restart the workers, which rescans every watch path.</summary>
        Restart,

        /// <summary>Stop parsing until resumed or the service restarts; changes are still queued.</summary>
        Pause,
        Resume,

        /// <summary>Queue this machine's failed Parse jobs again.</summary>
        RetryFailed,
    }

    public enum IndexerCommandStatus
    {
        Pending,
        Done,
        Failed,

        /// <summary>The machine doesn't allow remote management.</summary>
        Rejected,
    }
}
