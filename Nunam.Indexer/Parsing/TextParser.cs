using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Reads plain-text and source/markup files as UTF-8 content for full-text search and embedding.
    /// Files above the configured size cap are skipped (metadata-only) to keep the index lean.
    /// </summary>
    public class TextParser(IOptions<IndexerOptions> options, ILogger<TextParser> logger) : IFileParser
    {
        public string Name => IndexerProviders.Parsers.Text;

        private readonly IndexerOptions _options = options.Value;

        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
            ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx", ".cs", ".java", ".py", ".go",
            ".rs", ".rb", ".php", ".c", ".cpp", ".h", ".hpp", ".sh", ".ps1", ".bat", ".sql",
            ".ini", ".cfg", ".conf", ".toml",
        };

        public bool CanParse(FileKind kind, string extension) => TextExtensions.Contains(extension);

        public async Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > _options.MaxFileSizeBytes)
            {
                logger.LogDebug("Skipping content of oversized text file {Path} ({Size} bytes)", file.FullName, file.Length);
                return ParseOutcome.Empty;
            }

            var content = await File.ReadAllTextAsync(file.FullName, cancellationToken);
            return new ParseOutcome { Content = content };
        }
    }
}
