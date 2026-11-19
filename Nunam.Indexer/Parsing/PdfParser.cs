using System.Text;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Parsing;
using Syncfusion.PdfToImageConverter;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts the text of every page. A PDF with almost no text is most likely a scan, so its first
    /// page is rendered as the preview and sent to the vision model instead.
    /// </summary>
    public class PdfParser(IOptions<IndexerOptions> options, ILogger<PdfParser> logger) : IFileParser
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
                var text = new StringBuilder();
                foreach (PdfLoadedPage page in document.Pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    text.AppendLine(page.ExtractText());
                    if (text.Length >= _options.MaxContentChars) break;
                }

                var info = document.DocumentInformation;
                var metadata = ParseMetadata.ToJson(
                    ("pages", document.Pages.Count),
                    ("title", info.Title),
                    ("author", info.Author),
                    ("subject", info.Subject));

                var content = text.ToString().Trim();
                if (content.Length >= ScannedTextThreshold || document.Pages.Count == 0)
                {
                    return Task.FromResult(new ParseOutcome { Content = content, Metadata = metadata });
                }

                var preview = RenderFirstPage(stream, file);
                return Task.FromResult(new ParseOutcome
                {
                    Content = content,
                    Metadata = metadata,
                    PreviewImage = preview,
                    NeedsDescribe = preview is not null,
                });
            }
        }

        private byte[]? RenderFirstPage(Stream stream, FileInfo file)
        {
            try
            {
                stream.Position = 0;
                using var converter = new PdfToImageConverter();
                converter.Load(stream);
                using var image = converter.Convert(0, false, true);
                using var buffer = new MemoryStream();
                image.CopyTo(buffer);

                return buffer.Length <= _options.MaxPreviewBytes ? buffer.ToArray() : null;
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
    }
}
