using NpgsqlTypes;
using Pgvector;

namespace Nunam.Core.Entities
{
    public class IndexedFile : EntityBase
    {
        public Guid MachineId { get; set; }
        public Machine Machine { get; set; } = default!;

        public string Path { get; set; } = default!;
        public string FileName { get; set; } = default!;
        public string? Extension { get; set; }
        public long? SizeBytes { get; set; }
        public FileType FileType { get; set; }

        public DateTimeOffset? FileCreatedAt { get; set; }
        public DateTimeOffset? FileModifiedAt { get; set; }
        public DateTimeOffset IndexedAt { get; set; }

        public string? Content { get; set; }
        public string? Description { get; set; }
        public string? Metadata { get; set; }

        public Vector? Embedding { get; set; }
        public NpgsqlTsVector? Fts { get; set; }
    }
}
