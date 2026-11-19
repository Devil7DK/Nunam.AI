using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Syncfusion.OCRProcessor;
using Syncfusion.Pdf.Parsing;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Reads text out of images and scanned PDFs with Syncfusion's bundled Tesseract.</summary>
    public partial class OcrService(IOptions<IndexerOptions> options, ILogger<OcrService> logger)
    {
        private const int MinWords = 3;
        private readonly IndexerOptions _options = options.Value;

        /// <summary>
        /// Syncfusion's OCR imports <c>libdl</c> by its unversioned name, which glibc 2.34+ no longer
        /// installs outside the -devel package; point it at the runtime <c>libdl.so.2</c> instead.
        /// </summary>
        static OcrService()
        {
            if (!OperatingSystem.IsLinux()) return;

            NativeLibrary.SetDllImportResolver(typeof(OCRProcessor).Assembly, (name, _, _) =>
                name == "libdl" && NativeLibrary.TryLoad("libdl.so.2", out var handle) ? handle : IntPtr.Zero);
        }

        private string TessDataPath =>
            _options.OcrTessDataPath ?? Path.Combine(AppContext.BaseDirectory, "runtimes", "tessdata");

        /// <summary>Recognized text, or <c>null</c> when OCR is off, fails, or finds no real words.</summary>
        public string? ReadImage(byte[] image, string path)
        {
            if (!_options.EnableOcr) return null;

            try
            {
                using var processor = CreateProcessor();
                using var stream = new MemoryStream(image);
                return KeepIfWords(processor.PerformOCR(stream, TessDataPath));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "OCR failed for {Path}", path);
                return null;
            }
        }

        /// <summary>
        /// Adds a text layer to the first <see cref="IndexerOptions.OcrMaxPdfPages"/> pages in place, so
        /// the caller can extract text from them as usual. Returns false when OCR is off or fails.
        /// </summary>
        public bool AddTextLayer(PdfLoadedDocument document, string path)
        {
            if (!_options.EnableOcr || document.Pages.Count == 0) return false;

            try
            {
                using var processor = CreateProcessor();
                var lastPage = Math.Min(document.Pages.Count, _options.OcrMaxPdfPages) - 1;
                processor.PerformOCR(document, 0, lastPage, TessDataPath);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OCR failed for {Path}; indexing without recognized text", path);
                return false;
            }
        }

        private OCRProcessor CreateProcessor()
        {
            var processor = new OCRProcessor();
            processor.Settings.Language = _options.OcrLanguage;
            return processor;
        }

        /// <summary>Tesseract turns photos without text into stray letters; a few real words mean real text.</summary>
        private static string? KeepIfWords(string? text) =>
            text is not null && Word().Count(text) >= MinWords ? text.Trim() : null;

        [GeneratedRegex(@"\p{L}{3,}")]
        private static partial Regex Word();
    }
}
