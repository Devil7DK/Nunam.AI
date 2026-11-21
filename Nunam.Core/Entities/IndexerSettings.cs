namespace Nunam.Core.Entities
{
    /// <summary>
    /// Settings set from the UI for one machine's indexer, applied over its local settings when that
    /// machine allows remote management. Written by the API, read by the indexer.
    /// </summary>
    public class IndexerSettings
    {
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        /// <summary>JSON <see cref="Management.RemoteIndexerSettings"/>.</summary>
        public string Settings { get; set; } = default!;

        /// <summary>Goes up with every change, so the indexer can tell it hasn't applied the latest.</summary>
        public int Revision { get; set; }

        public string UpdatedBy { get; set; } = default!;
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
