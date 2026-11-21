namespace Nunam.Core.Entities
{
    /// <summary>What a machine's indexer last reported about itself; one row per machine, rewritten on each report.</summary>
    public class IndexerStatus
    {
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        public DateTimeOffset ReportedAt { get; set; }
        public DateTimeOffset ProcessStartedAt { get; set; }

        /// <summary>When the indexer last (re)started its workers: at process start, and after each restart from the UI.</summary>
        public DateTimeOffset StartedAt { get; set; }

        public string Version { get; set; } = default!;
        public string OperatingSystem { get; set; } = default!;
        public string ServiceAccount { get; set; } = default!;
        public IndexerState State { get; set; }

        /// <summary>Whether the machine's own settings allow it to be managed from the UI.</summary>
        public bool RemoteManagement { get; set; }

        /// <summary>The <see cref="IndexerSettings.Revision"/> the running indexer applied, or null for none.</summary>
        public int? AppliedRevision { get; set; }

        public DateTimeOffset? ScanStartedAt { get; set; }
        public DateTimeOffset? ScanFinishedAt { get; set; }

        /// <summary>JSON array of <see cref="Management.ConfigEntry"/>: the settings in effect, without secrets.</summary>
        public string Config { get; set; } = default!;

        /// <summary>JSON array of <see cref="Management.WatchPathReport"/>.</summary>
        public string WatchPaths { get; set; } = default!;
    }

    public enum IndexerState
    {
        Running,
        Scanning,
        Paused,
    }
}
