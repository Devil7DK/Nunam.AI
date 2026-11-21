using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Word 2007 and later (.docx, .docm, .dotx, .dotm), and Open XML files saved under the old .doc name.</summary>
    public class OpenXmlWordParser(ILogger<OpenXmlWordParser> logger) : IFileParser
    {
        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".docx", ".docm", ".dotx", ".dotm", ".doc", ".dot" };

        public string Name => IndexerProviders.Parsers.OpenXmlWord;

        public bool CanParse(FileKind kind, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            var limits = request.Limits;
            if (file.Length > limits.MaxFileSizeBytes) return Task.FromResult(ParseOutcome.Empty);

            if (!FileSignature.IsZip(file))
            {
                if (file.Extension.ToLowerInvariant() is ".doc" or ".dot") return Task.FromResult(ParseOutcome.Declined);

                // A password-protected .docx is an OLE file holding the encrypted package.
                logger.LogInformation("Skipping content of encrypted document {Path}", file.FullName);
                return Task.FromResult(new ParseOutcome { Metadata = ParseOutcome.MetadataOf(("encrypted", true)) });
            }

            using var document = WordprocessingDocument.Open(file.FullName, false, OfficeText.Settings);
            var main = document.MainDocumentPart;

            var text = new StringBuilder();
            if (main is not null)
            {
                OfficeText.Append(text, main, limits.MaxContentChars, cancellationToken);
                if (main.FootnotesPart is { } footnotes) OfficeText.Append(text, footnotes, limits.MaxContentChars, cancellationToken);
                if (main.EndnotesPart is { } endnotes) OfficeText.Append(text, endnotes, limits.MaxContentChars, cancellationToken);
            }

            var (title, author, subject) = OfficeText.CoreProperties(document);
            var words = int.TryParse(document.ExtendedFilePropertiesPart?.Properties?.Words?.Text, out var n) && n > 0 ? n : (int?)null;
            return Task.FromResult(new ParseOutcome
            {
                Content = text.ToString().Trim(),
                Metadata = ParseOutcome.MetadataOf(("title", title), ("author", author), ("subject", subject), ("words", words)),
            });
        }
    }

    /// <summary>Binary Word 97–2003 documents (.doc, .dot). Declines Word 6/95 and Open XML files.</summary>
    public class Word97Parser(ILogger<Word97Parser> logger) : IFileParser
    {
        public string Name => IndexerProviders.Parsers.Word97;

        public bool CanParse(FileKind kind, string extension) =>
            extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) || extension.Equals(".dot", StringComparison.OrdinalIgnoreCase);

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > request.Limits.MaxFileSizeBytes) return Task.FromResult(ParseOutcome.Empty);
            if (FileSignature.IsZip(file)) return Task.FromResult(ParseOutcome.Declined);

            Word97.Result result;
            try
            {
                using var stream = file.OpenRead();
                result = Word97.Read(stream, request.Limits.MaxContentChars);
            }
            catch (InvalidDataException ex)
            {
                logger.LogDebug(ex, "Can't read {Path} as a Word 97 document", file.FullName);
                return Task.FromResult(ParseOutcome.Declined);
            }

            if (result.Encrypted)
            {
                logger.LogInformation("Skipping content of encrypted document {Path}", file.FullName);
                return Task.FromResult(new ParseOutcome { Metadata = ParseOutcome.MetadataOf(("encrypted", true)) });
            }

            var summary = result.Summary;
            return Task.FromResult(new ParseOutcome
            {
                Content = result.Text,
                Metadata = ParseOutcome.MetadataOf(
                    ("title", summary.Title), ("author", summary.Author), ("subject", summary.Subject), ("words", summary.Words)),
            });
        }
    }

    /// <summary>Rich Text Format.</summary>
    public class RtfParser : IFileParser
    {
        public string Name => IndexerProviders.Parsers.Rtf;

        public bool CanParse(FileKind kind, string extension) => extension.Equals(".rtf", StringComparison.OrdinalIgnoreCase);

        public async Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > request.Limits.MaxFileSizeBytes) return ParseOutcome.Empty;

            // RTF is 7-bit text; Latin-1 keeps any stray high byte as the same value for \'hh decoding.
            var rtf = await File.ReadAllTextAsync(file.FullName, Encoding.Latin1, cancellationToken);
            return new ParseOutcome { Content = RtfText.Extract(rtf, request.Limits.MaxContentChars) };
        }
    }

    internal static class FileSignature
    {
        /// <summary>Open XML documents are zip files.</summary>
        public static bool IsZip(FileInfo file)
        {
            using var stream = file.OpenRead();
            Span<byte> magic = stackalloc byte[4];
            return stream.Read(magic) == 4 && magic.SequenceEqual("PK\x03\x04"u8);
        }
    }
}
