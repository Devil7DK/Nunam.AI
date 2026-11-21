using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;
using Nunam.Indexer.Providers;

namespace Nunam.Indexer.Parsing
{
    /// <summary>OCR with the <c>tesseract</c> command-line program: the image on stdin, the text on stdout.</summary>
    public partial class TesseractOcr(IOptions<IndexerOptions> options, ILogger<TesseractOcr> logger) : IOcrEngine
    {
        private const int MinWords = 3;
        private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(120);

        // Tesseract's OpenMP threads would otherwise take every core for each page.
        private static readonly Dictionary<string, string> Environment = new() { ["OMP_THREAD_LIMIT"] = "1" };

        private readonly IndexerOptions _options = options.Value;
        private readonly Lazy<string?> _executable = new(() => FindExecutable(options.Value, logger));

        public string Name => IndexerProviders.Ocr.Tesseract;

        public bool IsAvailable => _executable.Value is not null;

        public async Task<string?> ReadImageAsync(byte[] image, CancellationToken cancellationToken)
        {
            if (!IsAvailable) return null;

            var arguments = new List<string> { "stdin", "stdout", "-l", _options.OcrLanguage };
            if (!string.IsNullOrWhiteSpace(_options.OcrTessDataPath))
            {
                arguments.AddRange(["--tessdata-dir", _options.OcrTessDataPath]);
            }

            try
            {
                var text = await ProcessRunner.RunAsync(
                    _executable.Value!, arguments, image, PageTimeout, _options.MaxContentChars, Environment, cancellationToken);
                return KeepIfWords(text);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "OCR failed");
                return null;
            }
        }

        private static string? FindExecutable(IndexerOptions options, ILogger logger)
        {
            var name = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.OcrTesseractPath)) candidates.Add(options.OcrTesseractPath);
            candidates.AddRange((System.Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(dir => Path.Combine(dir, name)));
            if (OperatingSystem.IsWindows())
            {
                // The usual Windows installer doesn't add itself to PATH.
                candidates.Add(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", name));
            }

            var found = candidates.FirstOrDefault(File.Exists);
            if (found is null)
            {
                logger.LogWarning("Tesseract not found (set Indexer:OcrTesseractPath or install it); images and scans are indexed without OCR");
            }
            else
            {
                logger.LogInformation("OCR with {Tesseract}, languages {Languages}", found, options.OcrLanguage);
            }
            return found;
        }

        /// <summary>Tesseract turns photos without text into stray letters; a few real words mean real text.</summary>
        public static string? KeepIfWords(string? text) =>
            text is not null && Word().Count(text) >= MinWords ? text.Trim() : null;

        [GeneratedRegex(@"\p{L}{3,}")]
        private static partial Regex Word();
    }

    /// <summary>OCR turned off.</summary>
    public sealed class NoOcr : IOcrEngine
    {
        public string Name => IndexerProviders.None;
        public bool IsAvailable => false;
        public Task<string?> ReadImageAsync(byte[] image, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    /// <summary>PDF rendering turned off: scanned PDFs get no preview and no OCR.</summary>
    public sealed class NoPdfRenderer : IPdfRenderer
    {
        public string Name => IndexerProviders.None;
        public bool IsAvailable => false;
        public IRenderedPdf? Open(byte[] pdf) => null;
    }
}
