using System.Text;
using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts the text of every page with PdfPig. A PDF with almost no text is most likely a scan: its
    /// first page is rendered as the preview for the vision model, and its pages are OCRed for text.
    /// </summary>
    public class PdfParser(IOptions<IndexerOptions> options, ILogger<PdfParser> logger) : IFileParser
    {
        private const int ScannedTextThreshold = 50;

        /// <summary>About 240 dpi on an A4 page, which Tesseract reads well.</summary>
        private const int OcrEdgePx = 2400;

        private readonly IndexerOptions _options = options.Value;

        public string Name => IndexerProviders.Parsers.PdfPig;

        public bool CanParse(FileKind kind, string extension) =>
            extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        public async Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            var limits = request.Limits;
            if (file.Length > limits.MaxFileSizeBytes)
            {
                return ParseOutcome.Empty;
            }

            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);

            string content;
            IReadOnlyDictionary<string, object?>? metadata;
            int pages;
            try
            {
                using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true });
                pages = document.NumberOfPages;
                content = ExtractText(document, limits.MaxContentChars, cancellationToken);
                metadata = ParseOutcome.MetadataOf(
                    ("pages", pages),
                    ("title", document.Information.Title),
                    ("author", document.Information.Author),
                    ("subject", document.Information.Subject));
            }
            catch (PdfDocumentEncryptedException)
            {
                logger.LogInformation("Skipping content of encrypted PDF {Path}", file.FullName);
                return new ParseOutcome { Metadata = ParseOutcome.MetadataOf(("encrypted", true)) };
            }

            if (content.Length >= ScannedTextThreshold || pages == 0 || !request.PdfRenderer.IsAvailable)
            {
                return new ParseOutcome { Content = content, Metadata = metadata };
            }

            using var rendered = request.PdfRenderer.Open(bytes);
            if (rendered is null)
            {
                logger.LogWarning("Could not render {Path}; indexing its text only", file.FullName);
                return new ParseOutcome { Content = content, Metadata = metadata };
            }

            var preview = RenderPreview(rendered, limits);
            var recognized = await RecognizePagesAsync(rendered, request.Ocr, limits, cancellationToken);
            return new ParseOutcome
            {
                Content = string.IsNullOrEmpty(recognized) ? content : recognized,
                Metadata = metadata,
                PreviewImage = preview,
                NeedsDescribe = preview is not null,
            };
        }

        private static string ExtractText(PdfDocument document, int limit, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                text.AppendLine(ContentOrderTextExtractor.GetText(page));
                if (text.Length >= limit) break;
            }

            return text.ToString().Trim();
        }

        private static byte[]? RenderPreview(IRenderedPdf document, ParseLimits limits)
        {
            var page = document.RenderPage(0, limits.PreviewMaxEdgePx);
            return page is null ? null : PreviewImages.Create(page, limits.PreviewMaxEdgePx, limits.MaxImagePixels)?.Bytes;
        }

        private async Task<string?> RecognizePagesAsync(
            IRenderedPdf document, IOcrEngine ocr, ParseLimits limits, CancellationToken cancellationToken)
        {
            if (!ocr.IsAvailable) return null;

            var text = new StringBuilder();
            var pages = Math.Min(document.PageCount, _options.OcrMaxPdfPages);
            for (var i = 0; i < pages && text.Length < limits.MaxContentChars; i++)
            {
                if (document.RenderPage(i, OcrEdgePx) is not { } image) continue;
                if (await ocr.ReadImageAsync(image, cancellationToken) is { } pageText)
                {
                    text.AppendLine(pageText);
                }
            }

            return text.Length == 0 ? null : text.ToString().Trim();
        }
    }
}
