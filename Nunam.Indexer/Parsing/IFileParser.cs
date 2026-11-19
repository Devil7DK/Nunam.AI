using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// What a parser extracted from a file. Any field may be empty; the parse pipeline decides
    /// what downstream jobs (embed/describe) to enqueue based on this.
    /// </summary>
    public record ParseOutcome
    {
        /// <summary>Extracted text content for full-text search and embedding, if any.</summary>
        public string? Content { get; init; }

        /// <summary>Image bytes to store for the vision model, if this file is describable.</summary>
        public byte[]? PreviewImage { get; init; }

        /// <summary>True when a vision <see cref="IndexJobKind.Describe"/> job should be enqueued.</summary>
        public bool NeedsDescribe { get; init; }

        /// <summary>Optional JSON metadata blob to persist on the indexed file.</summary>
        public string? Metadata { get; init; }

        public static ParseOutcome Empty { get; } = new();
    }

    /// <summary>A pluggable extractor for one family of file types.</summary>
    public interface IFileParser
    {
        /// <summary>Whether this parser handles the given detected type / extension.</summary>
        bool CanParse(FileType fileType, string extension);

        /// <summary>Extract content/preview from the file. Should throw only on I/O errors.</summary>
        Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken);
    }
}
