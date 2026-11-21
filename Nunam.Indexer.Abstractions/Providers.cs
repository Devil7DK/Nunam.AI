namespace Nunam.Indexer.Abstractions
{
    /// <summary>What kind of file the indexer took a file for, from its extension or contents.</summary>
    public enum FileKind
    {
        /// <summary>Anything not below; parsed by name unless a parser claims its extension.</summary>
        Other = 0,

        /// <summary>Text, PDF, Office and similar files.</summary>
        Document = 1,

        /// <summary>Photos and other pictures.</summary>
        Image = 2,

        /// <summary>Music and recordings.</summary>
        Audio = 3,

        /// <summary>Video files.</summary>
        Video = 4,
    }

    /// <summary>
    /// Reads one family of file formats. The indexer tries the parsers configured for an extension in
    /// order (<c>Indexer:Parsers</c>); without configuration, every parser whose <see cref="CanParse"/>
    /// accepts the file, plugins and commands before the built-in ones.
    /// </summary>
    public interface IFileParser
    {
        /// <summary>Unique, lower-case name used in settings, such as <c>pdfpig</c> or <c>epub</c>.</summary>
        string Name { get; }

        /// <summary>Whether this parser offers itself for the file when no parser list is configured for its extension.</summary>
        bool CanParse(FileKind kind, string extension);

        /// <summary>
        /// Reads the file. Return <see cref="ParseOutcome.Declined"/> (or throw) to let the next parser
        /// try; a parser that throws and no other succeeds fails the file's job with that error.
        /// </summary>
        Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Reads text out of an image.</summary>
    public interface IOcrEngine
    {
        /// <summary>Unique, lower-case name used in <c>Indexer:OcrEngine</c>.</summary>
        string Name { get; }

        /// <summary>False when the engine can't run here (e.g. its program isn't installed).</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// The recognized text, or <c>null</c> when there is none worth keeping or recognition failed.
        /// <paramref name="image"/> is an encoded image (PNG or JPEG). Throw only on cancellation.
        /// </summary>
        Task<string?> ReadImageAsync(byte[] image, CancellationToken cancellationToken);
    }

    /// <summary>Renders PDF pages to images, for previews and OCR of scanned documents.</summary>
    public interface IPdfRenderer
    {
        /// <summary>Unique, lower-case name used in <c>Indexer:PdfRenderer</c>.</summary>
        string Name { get; }

        /// <summary>False when the renderer can't run here.</summary>
        bool IsAvailable { get; }

        /// <summary>The document, or <c>null</c> when it can't be read or needs a password.</summary>
        IRenderedPdf? Open(byte[] pdf);
    }

    /// <summary>An open document from <see cref="IPdfRenderer.Open"/>.</summary>
    public interface IRenderedPdf : IDisposable
    {
        /// <summary>Number of pages in the document.</summary>
        int PageCount { get; }

        /// <summary>
        /// The page as PNG, scaled so its longer edge is <paramref name="maxEdge"/> pixels, or <c>null</c>
        /// when the page can't be rendered.
        /// </summary>
        byte[]? RenderPage(int index, int maxEdge);
    }
}
