using Microsoft.Extensions.Logging;

namespace Nunam.Core.Entities
{
    /// <summary>A log entry an indexer sent to the database, so it can be read in the UI.</summary>
    public class IndexerLogEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        public DateTimeOffset At { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string? Exception { get; set; }
    }
}
