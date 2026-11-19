using Microsoft.Extensions.Options;
using Nunam.Core.Entities;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Extracts the text of Word documents (.doc, .docx and their macro/template variants, .rtf).</summary>
    public class WordParser(IOptions<IndexerOptions> options) : IFileParser
    {
        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".rtf" };

        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Task.FromResult(ParseOutcome.Empty);
            }

            using var stream = file.OpenRead();
            using var document = new WordDocument(stream, FormatType.Automatic);

            var properties = document.BuiltinDocumentProperties;
            return Task.FromResult(new ParseOutcome
            {
                Content = document.GetText(),
                Metadata = ParseMetadata.ToJson(
                    ("title", properties.Title),
                    ("author", properties.Author),
                    ("subject", properties.Subject),
                    ("words", properties.WordCount > 0 ? properties.WordCount : null)),
            });
        }
    }
}
