using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.PdfToImageConverter;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts the text of every page. A PDF with almost no text is most likely a scan: its first page
    /// is rendered as the preview for the vision model, and its pages are OCRed for text.
    /// </summary>
    public partial class PdfParser(IOptions<IndexerOptions> options, OcrService ocr, ILogger<PdfParser> logger) : IFileParser
    {
        private const int ScannedTextThreshold = 50;
        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) =>
            extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        public Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Task.FromResult(ParseOutcome.Empty);
            }

            using var stream = file.OpenRead();

            PdfLoadedDocument document;
            try
            {
                document = new PdfLoadedDocument(stream);
            }
            catch (Exception ex) when (IsPasswordProtected(ex))
            {
                logger.LogInformation("Skipping content of encrypted PDF {Path}", file.FullName);
                return Task.FromResult(new ParseOutcome { Metadata = ParseMetadata.ToJson(("encrypted", true)) });
            }

            using (document)
            {
                var text = ExtractText(document, cancellationToken);

                var info = document.DocumentInformation;
                var metadata = ParseMetadata.ToJson(
                    ("pages", document.Pages.Count),
                    ("title", info.Title),
                    ("author", info.Author),
                    ("subject", info.Subject));

                var content = text;
                if (content.Length >= ScannedTextThreshold || document.Pages.Count == 0)
                {
                    return Task.FromResult(new ParseOutcome { Content = content, Metadata = metadata });
                }

                var preview = RenderFirstPage(file);
                if (ocr.AddTextLayer(document, file.FullName))
                {
                    // The OCR layer is one text object per word, which extracts as a line per word.
                    content = Whitespace().Replace(ExtractText(document, cancellationToken), " ");
                }
                return Task.FromResult(new ParseOutcome
                {
                    Content = content,
                    Metadata = metadata,
                    PreviewImage = preview,
                    NeedsDescribe = preview is not null,
                });
            }
        }

        private string ExtractText(PdfLoadedDocument document, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            foreach (PdfLoadedPage page in document.Pages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.AppendLine(page.ExtractText());
                if (text.Length >= _options.MaxContentChars) break;
            }

            return text.ToString().Trim();
        }

        /// <summary>Uses its own stream: disposing the converter closes the stream it loaded.</summary>
        private byte[]? RenderFirstPage(FileInfo file)
        {
            try
            {
                using var stream = file.OpenRead();
                using var converter = new PdfToImageConverter();
                converter.Load(stream);
                // Rendered straight at preview size: the page's own size comes from the file and can be huge.
                var edge = _options.PreviewMaxEdgePx;
                using var image = converter.Convert(0, new Syncfusion.Drawing.SizeF(edge, edge), true, false, true);
                using var buffer = new MemoryStream();
                image.CopyTo(buffer);

                return PreviewImages.Create(buffer.ToArray(), edge, _options.MaxImagePixels)?.Bytes;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not render the first page of {Path}; indexing text only", file.FullName);
                return null;
            }
        }

        private static bool IsPasswordProtected(Exception ex) =>
            ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase);

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();
    }
}
