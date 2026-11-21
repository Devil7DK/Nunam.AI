namespace Nunam.Indexer.Abstractions
{
    /// <summary>A file to parse, the limits that apply to it, and the OCR engine and PDF renderer in use.</summary>
    public sealed record ParseRequest(
        FileInfo File,
        FileKind Kind,
        ParseLimits Limits,
        IOcrEngine Ocr,
        IPdfRenderer PdfRenderer);

    /// <summary>The machine's size limits. A parser should stop reading once it has <see cref="MaxContentChars"/>.</summary>
    public sealed record ParseLimits(
        long MaxFileSizeBytes,
        int MaxContentChars,
        int PreviewMaxEdgePx,
        long MaxImagePixels);

    /// <summary>What a parser found. Every part is optional.</summary>
    public sealed record ParseOutcome
    {
        /// <summary>Text for full-text and semantic search.</summary>
        public string? Content { get; init; }

        /// <summary>
        /// An encoded image (PNG, JPEG, WebP…) for the vision model to describe. The indexer scales,
        /// rotates and re-encodes previews from plugins and commands; built-in parsers hand in final ones.
        /// </summary>
        public byte[]? PreviewImage { get; init; }

        /// <summary>Ask the vision model to describe <see cref="PreviewImage"/>.</summary>
        public bool NeedsDescribe { get; init; }

        /// <summary>Details such as page count or camera model; null and empty values are dropped.</summary>
        public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

        /// <summary>True for <see cref="Declined"/>.</summary>
        public bool IsDeclined { get; private init; }

        /// <summary>Indexes the file by name only.</summary>
        public static ParseOutcome Empty { get; } = new();

        /// <summary>This parser can't read the file; the next one configured for it gets to try.</summary>
        public static ParseOutcome Declined { get; } = new() { IsDeclined = true };

        /// <summary>Builds <see cref="Metadata"/> from key/value pairs, skipping null and empty values.</summary>
        public static IReadOnlyDictionary<string, object?>? MetadataOf(params (string Key, object? Value)[] fields)
        {
            var present = fields
                .Where(f => f.Value is not null && f.Value is not string { Length: 0 })
                .ToDictionary(f => f.Key, f => f.Value);
            return present.Count == 0 ? null : present;
        }
    }
}
