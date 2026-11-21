using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;
using Nunam.Indexer.Parsing;

namespace Nunam.Indexer.Providers
{
    /// <summary>A program from <c>Indexer:Commands</c> that prints a file's text on stdout.</summary>
    public sealed class CommandParser(string name, CommandOptions command) : IFileParser
    {
        private readonly HashSet<string> _extensions = new(command.Extensions.Select(Normalize), StringComparer.OrdinalIgnoreCase);

        public string Name { get; } = IndexerProviders.CommandPrefix + name;

        public bool CanParse(FileKind kind, string extension) => _extensions.Contains(extension);

        public async Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > request.Limits.MaxFileSizeBytes) return ParseOutcome.Empty;

            var input = command.Stdin ? await File.ReadAllBytesAsync(file.FullName, cancellationToken) : null;
            var text = await ProcessRunner.RunAsync(
                command.Path,
                command.EffectiveArguments.Select(a => a.Replace("{file}", file.FullName)),
                input,
                TimeSpan.FromSeconds(command.TimeoutSeconds),
                request.Limits.MaxContentChars,
                null,
                cancellationToken);
            return new ParseOutcome { Content = text.Trim() };
        }

        private static string Normalize(string extension) =>
            extension.StartsWith('.') ? extension : "." + extension;
    }

    /// <summary>A program from <c>Indexer:Commands</c> with <c>Use: ocr</c>: the image on stdin, the text on stdout.</summary>
    public sealed class CommandOcr(string name, CommandOptions command, string languages, int maxChars, ILogger logger) : IOcrEngine
    {
        public string Name { get; } = IndexerProviders.CommandPrefix + name;

        public bool IsAvailable => File.Exists(command.Path);

        public async Task<string?> ReadImageAsync(byte[] image, CancellationToken cancellationToken)
        {
            try
            {
                var text = await ProcessRunner.RunAsync(
                    command.Path,
                    command.EffectiveArguments.Select(a => a.Replace("{lang}", languages)),
                    image,
                    TimeSpan.FromSeconds(command.TimeoutSeconds),
                    maxChars,
                    null,
                    cancellationToken);
                return TesseractOcr.KeepIfWords(text);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug(ex, "OCR command {Name} failed", Name);
                return null;
            }
        }
    }
}
