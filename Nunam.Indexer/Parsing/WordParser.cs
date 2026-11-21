using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Extracts the text of Word documents (.doc, .docx and their macro/template variants, .rtf).</summary>
    public class WordParser(IOptions<IndexerOptions> options, ILogger<WordParser> logger) : IFileParser
    {
        private static readonly HashSet<string> OpenXml = new(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".docm", ".dotx", ".dotm" };

        private static readonly HashSet<string> Binary = new(StringComparer.OrdinalIgnoreCase) { ".doc", ".dot" };

        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) =>
            OpenXml.Contains(extension) || Binary.Contains(extension)
            || extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase);

        public async Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return ParseOutcome.Empty;
            }

            var extension = file.Extension;
            if (extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase))
            {
                var rtf = await File.ReadAllTextAsync(file.FullName, Encoding.Latin1, cancellationToken);
                return new ParseOutcome { Content = RtfText.Extract(rtf, _options.MaxContentChars) };
            }

            var zip = IsZip(file);
            if (OpenXml.Contains(extension) && !zip)
            {
                // A password-protected .docx is an OLE file holding the encrypted package.
                logger.LogInformation("Skipping content of encrypted document {Path}", file.FullName);
                return new ParseOutcome { Metadata = ParseMetadata.ToJson(("encrypted", true)) };
            }
            return zip ? ReadOpenXml(file, cancellationToken) : ReadBinary(file);
        }

        private ParseOutcome ReadOpenXml(FileInfo file, CancellationToken cancellationToken)
        {
            using var document = WordprocessingDocument.Open(file.FullName, false, OfficeText.Settings);
            var main = document.MainDocumentPart;

            var text = new StringBuilder();
            if (main is not null)
            {
                OfficeText.Append(text, main, _options.MaxContentChars, cancellationToken);
                if (main.FootnotesPart is { } footnotes) OfficeText.Append(text, footnotes, _options.MaxContentChars, cancellationToken);
                if (main.EndnotesPart is { } endnotes) OfficeText.Append(text, endnotes, _options.MaxContentChars, cancellationToken);
            }

            var (title, author, subject) = OfficeText.CoreProperties(document);
            var words = int.TryParse(document.ExtendedFilePropertiesPart?.Properties?.Words?.Text, out var n) && n > 0 ? n : (int?)null;
            return new ParseOutcome
            {
                Content = text.ToString().Trim(),
                Metadata = ParseMetadata.ToJson(("title", title), ("author", author), ("subject", subject), ("words", words)),
            };
        }

        private ParseOutcome ReadBinary(FileInfo file)
        {
            Word97.Result result;
            try
            {
                using var stream = file.OpenRead();
                result = Word97.Read(stream, _options.MaxContentChars);
            }
            catch (InvalidDataException ex)
            {
                // Word 6/95 files and anything else that isn't Word 97 or later: indexed by name only.
                logger.LogDebug(ex, "Can't read {Path} as a Word 97 document", file.FullName);
                return ParseOutcome.Empty;
            }

            if (result.Encrypted)
            {
                logger.LogInformation("Skipping content of encrypted document {Path}", file.FullName);
                return new ParseOutcome { Metadata = ParseMetadata.ToJson(("encrypted", true)) };
            }

            var summary = result.Summary;
            return new ParseOutcome
            {
                Content = result.Text,
                Metadata = ParseMetadata.ToJson(
                    ("title", summary.Title),
                    ("author", summary.Author),
                    ("subject", summary.Subject),
                    ("words", summary.Words)),
            };
        }

        /// <summary>Open XML documents are zip files; some programs save them under the old .doc name.</summary>
        private static bool IsZip(FileInfo file)
        {
            using var stream = file.OpenRead();
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == 4 && magic.SequenceEqual("PK\x03\x04"u8);
        }
    }
}
