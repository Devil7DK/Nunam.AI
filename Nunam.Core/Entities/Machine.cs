namespace Nunam.Core.Entities
{
    public class Machine : EntityBase
    {
        public string Name { get; set; } = default!;
        public string Hostname { get; set; } = default!;
        public DateTimeOffset? LastSeen { get; set; }

        /// <summary>SHA-256 of the token the machine's indexer signs in to the API with; null until it enrolls.</summary>
        public byte[]? TokenHash { get; set; }
        public DateTimeOffset? TokenIssuedAt { get; set; }

        public ICollection<IndexedFile> Files { get; set; } = [];
        public ICollection<IndexJob> Jobs { get; set; } = [];
        public ICollection<MachineAccess> Access { get; set; } = [];
    }
}
