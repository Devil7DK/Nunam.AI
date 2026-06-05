namespace Nunam.Core.Entities
{
    public class Machine : EntityBase
    {
        public string Name { get; set; } = default!;
        public string Hostname { get; set; } = default!;
        public DateTimeOffset? LastSeen { get; set; }

        public ICollection<IndexedFile> Files { get; set; } = [];
        public ICollection<IndexJob> Jobs { get; set; } = [];
    }
}
