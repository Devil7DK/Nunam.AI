using System.IO.Compression;
using System.Text;
using System.Xml;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Plugins.OpenDocument
{
    /// <summary>
    /// Reads the text of OpenDocument files (LibreOffice and others): .odt, .ods, .odp and their
    /// templates. Paragraphs become lines; spreadsheet cells are separated by tabs, rows by lines.
    /// </summary>
    public sealed class OpenDocumentParser : IFileParser
    {
        private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        private const string DcNs = "http://purl.org/dc/elements/1.1/";
        private const string MetaNs = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".odt", ".ods", ".odp", ".ott", ".ots", ".otp" };

        // A zip bomb would have to expand past this before reading stops being worth it.
        private static readonly XmlReaderSettings Settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 200_000_000,
            IgnoreWhitespace = false,
        };

        public string Name => "opendocument";

        public bool CanParse(FileKind kind, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > request.Limits.MaxFileSizeBytes) return Task.FromResult(ParseOutcome.Empty);

            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(file.FullName);
            }
            catch (InvalidDataException)
            {
                // Flat XML (.fodt) or not OpenDocument at all: let the next parser try.
                return Task.FromResult(ParseOutcome.Declined);
            }

            using (archive)
            {
                if (archive.GetEntry("content.xml") is not { } content) return Task.FromResult(ParseOutcome.Declined);
                if (archive.GetEntry("META-INF/manifest.xml") is { } manifest && IsEncrypted(manifest))
                {
                    return Task.FromResult(new ParseOutcome { Metadata = ParseOutcome.MetadataOf(("encrypted", true)) });
                }

                var text = ReadText(content, request.Limits.MaxContentChars, cancellationToken);
                var (title, author) = archive.GetEntry("meta.xml") is { } meta ? ReadMeta(meta) : (null, null);
                return Task.FromResult(new ParseOutcome
                {
                    Content = text,
                    Metadata = ParseOutcome.MetadataOf(("title", title), ("author", author)),
                });
            }
        }

        private static string ReadText(ZipArchiveEntry entry, int limit, CancellationToken cancellationToken)
        {
            var text = new StringBuilder();
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, Settings);
            // Styles come before the body and hold text too, such as the separators of a date format.
            if (!reader.ReadToFollowing("body", OfficeNs)) return "";

            var nodes = 0;
            while (reader.Read() && text.Length < limit)
            {
                if ((++nodes & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

                switch (reader.NodeType)
                {
                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace:
                        text.Append(reader.Value);
                        break;
                    case XmlNodeType.Element when reader.NamespaceURI == TextNs:
                        switch (reader.LocalName)
                        {
                            case "tab": text.Append('\t'); break;
                            case "line-break": text.AppendLine(); break;
                            case "s":
                                var count = int.TryParse(reader.GetAttribute("c", TextNs), out var c) ? Math.Clamp(c, 1, 100) : 1;
                                text.Append(' ', count);
                                break;
                        }
                        break;
                    case XmlNodeType.EndElement when reader.NamespaceURI == TextNs && reader.LocalName is "p" or "h":
                        text.AppendLine();
                        break;
                    case XmlNodeType.EndElement when reader.NamespaceURI == TableNs && reader.LocalName == "table-cell":
                        TrimLineEnd(text);
                        text.Append('\t');
                        break;
                    case XmlNodeType.EndElement when reader.NamespaceURI == TableNs && reader.LocalName == "table-row":
                        TrimLineEnd(text);
                        text.AppendLine();
                        break;
                }
            }
            return text.ToString().Trim();
        }

        private static void TrimLineEnd(StringBuilder text)
        {
            while (text.Length > 0 && text[^1] is '\n' or '\r' or '\t') text.Length--;
        }

        private static (string? Title, string? Author) ReadMeta(ZipArchiveEntry entry)
        {
            string? title = null, author = null;
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, Settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader is { NamespaceURI: DcNs, LocalName: "title" }) title ??= reader.ReadElementContentAsString().Trim();
                else if (reader is { NamespaceURI: MetaNs, LocalName: "initial-creator" } or { NamespaceURI: DcNs, LocalName: "creator" })
                {
                    author ??= reader.ReadElementContentAsString().Trim();
                }
            }
            return (string.IsNullOrEmpty(title) ? null : title, string.IsNullOrEmpty(author) ? null : author);
        }

        /// <summary>An encrypted document lists encryption data for its parts in the manifest.</summary>
        private static bool IsEncrypted(ZipArchiveEntry manifest)
        {
            using var stream = manifest.Open();
            using var reader = XmlReader.Create(stream, Settings);
            while (reader.Read())
            {
                if (reader is { NodeType: XmlNodeType.Element, LocalName: "encryption-data" }) return true;
            }
            return false;
        }
    }
}
