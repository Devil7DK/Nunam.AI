using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Reads text out of images with the <c>tesseract</c> command-line program.</summary>
    public partial class OcrService(IOptions<IndexerOptions> options, ILogger<OcrService> logger)
    {
        private const int MinWords = 3;
        private static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(120);

        private readonly IndexerOptions _options = options.Value;
        private readonly Lazy<string?> _executable = new(() => FindExecutable(options.Value, logger));

        /// <summary>Whether OCR is on and Tesseract was found.</summary>
        public bool IsAvailable => _options.EnableOcr && _executable.Value is not null;

        /// <summary>
        /// Recognized text, or <c>null</c> when OCR is off, fails, or finds no real words. Only a cancelled
        /// <paramref name="cancellationToken"/> throws.
        /// </summary>
        public async Task<string?> ReadImageAsync(byte[] image, string path, CancellationToken cancellationToken)
        {
            if (!IsAvailable) return null;

            try
            {
                return KeepIfWords(await RunAsync(image, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "OCR failed for {Path}", path);
                return null;
            }
        }

        private async Task<string> RunAsync(byte[] image, CancellationToken cancellationToken)
        {
            var start = new ProcessStartInfo(_executable.Value!)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in new[] { "stdin", "stdout", "-l", _options.OcrLanguage })
            {
                start.ArgumentList.Add(arg);
            }
            if (!string.IsNullOrWhiteSpace(_options.OcrTessDataPath))
            {
                start.ArgumentList.Add("--tessdata-dir");
                start.ArgumentList.Add(_options.OcrTessDataPath);
            }
            // Tesseract's OpenMP threads would otherwise take every core for each page.
            start.Environment["OMP_THREAD_LIMIT"] = "1";

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PageTimeout);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("tesseract did not start");
            try
            {
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.StandardInput.BaseStream.WriteAsync(image, timeout.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token);

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException($"tesseract exited with {process.ExitCode}: {(await errors).Trim()}");
                }
                return await output;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }

        private static string? FindExecutable(IndexerOptions options, ILogger logger)
        {
            if (!options.EnableOcr) return null;

            var name = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(options.OcrTesseractPath)) candidates.Add(options.OcrTesseractPath);
            candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(dir => Path.Combine(dir, name)));
            if (OperatingSystem.IsWindows())
            {
                // The usual Windows installer doesn't add itself to PATH.
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", name));
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
        private static string? KeepIfWords(string? text) =>
            text is not null && Word().Count(text) >= MinWords ? text.Trim() : null;

        [GeneratedRegex(@"\p{L}{3,}")]
        private static partial Regex Word();
    }
}
