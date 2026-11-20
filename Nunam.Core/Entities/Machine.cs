namespace Nunam.Core.Entities
{
    public class Machine : EntityBase
    {
        public string Name { get; set; } = default!;
        public string Hostname { get; set; } = default!;
        public DateTimeOffset? LastSeen { get; set; }

        /// <summary>
        /// The database login that registered this machine; set by the database, never by the client.
        /// Row-level security lets an indexer login see and write only its own machine's rows.
        /// </summary>
        public string? DbRole { get; private set; }

        public ICollection<IndexedFile> Files { get; set; } = [];
        public ICollection<IndexJob> Jobs { get; set; } = [];
        public ICollection<MachineAccess> Access { get; set; } = [];
    }
}
