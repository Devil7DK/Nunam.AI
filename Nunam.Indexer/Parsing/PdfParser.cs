using System.Text;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts the text of every page. A PDF with almost no text is most likely a scan: its first page
    /// is rendered as the preview for the vision model, and its pages are OCRed for text.
    /// </summary>
    public class PdfParser(IOptions<IndexerOptions> options, OcrService ocr, ILogger<PdfParser> logger) : IFileParser
    {
        private const int ScannedTextThreshold = 50;

        /// <summary>About 240 dpi on an A4 page, which Tesseract reads well.</summary>
        private const int OcrEdgePx = 2400;

        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) =>
            extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        public async Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return ParseOutcome.Empty;
            }

            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);

            string content;
            string? metadata;
            int pages;
            try
            {
                using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true });
                pages = document.NumberOfPages;
                content = ExtractText(document, cancellationToken);
                metadata = ParseMetadata.ToJson(
                    ("pages", pages),
                    ("title", document.Information.Title),
                    ("author", document.Information.Author),
                    ("subject", document.Information.Subject));
            }
            catch (PdfDocumentEncryptedException)
            {
                logger.LogInformation("Skipping content of encrypted PDF {Path}", file.FullName);
                return new ParseOutcome { Metadata = ParseMetadata.ToJson(("encrypted", true)) };
            }

            if (content.Length >= ScannedTextThreshold || pages == 0)
            {
                return new ParseOutcome { Content = content, Metadata = metadata };
            }

            using var rendered = Pdfium.Open(bytes);
            if (rendered is null)
            {
                logger.LogWarning("Could not render {Path}; indexing its text only", file.FullName);
                return new ParseOutcome { Content = content, Metadata = metadata };
            }

            var preview = RenderPreview(rendered);
            var recognized = await RecognizePagesAsync(rendered, file.FullName, cancellationToken);
            return new ParseOutcome
            {
                Content = string.IsNullOrEmpty(recognized) ? content : recognized,
                Metadata = metadata,
                PreviewImage = preview,
                NeedsDescribe = preview is not null,
            };
        }

        private string ExtractText(PdfDocument document, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
                if (text.Length >= _options.MaxContentChars) break;
            }

            return text.ToString().Trim();
        }

        private byte[]? RenderPreview(Pdfium document)
        {
            var page = document.RenderPage(0, _options.PreviewMaxEdgePx);
            return page is null ? null : PreviewImages.Create(page, _options.PreviewMaxEdgePx, _options.MaxImagePixels)?.Bytes;
        }

        private async Task<string?> RecognizePagesAsync(Pdfium document, string path, CancellationToken cancellationToken)
        {
            if (!ocr.IsAvailable) return null;

            var text = new StringBuilder();
            var pages = Math.Min(document.PageCount, _options.OcrMaxPdfPages);
            for (var i = 0; i < pages && text.Length < _options.MaxContentChars; i++)
            {
                if (document.RenderPage(i, OcrEdgePx) is not { } image) continue;
                if (await ocr.ReadImageAsync(image, path, cancellationToken) is { } pageText)
                {
                    text.AppendLine(pageText);
                }
            }

            return text.Length == 0 ? null : text.ToString().Trim();
        }
    }
}
