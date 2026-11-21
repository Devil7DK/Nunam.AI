using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Streams the text out of Office Open XML parts without building their element tree, so a part
    /// that unzips to gigabytes costs reading time, not memory.
    /// </summary>
    public static class OfficeText
    {
        /// <summary>
        /// Refuses a part beyond this many characters of XML. Text stops being collected far earlier, at
        /// <see cref="IndexerOptions.MaxContentChars"/>, so only a zip bomb gets near it.
        /// </summary>
        public static OpenSettings Settings { get; } = new() { MaxCharactersInPart = 200_000_000 };

        /// <summary>Appends a part's text, a line per paragraph, until <paramref name="text"/> reaches <paramref name="limit"/>.</summary>
        public static void Append(StringBuilder text, OpenXmlPart part, int limit, CancellationToken cancellationToken)
        {
            using var reader = OpenXmlReader.Create(part);
            var elements = 0;
            while (reader.Read() && text.Length < limit)
            {
                if ((++elements & 0xFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

                var type = reader.ElementType;
                if (reader.IsStartElement)
                {
                    if (type == typeof(W.Text) || type == typeof(A.Text))
                    {
                        text.Append(reader.GetText());
                    }
                    else if (type == typeof(W.TabChar))
                    {
                        text.Append('\t');
                    }
                    else if (type == typeof(W.Break) || type == typeof(A.Break))
                    {
                        text.AppendLine();
                    }
                }
                else if (reader.IsEndElement && (type == typeof(W.Paragraph) || type == typeof(A.Paragraph)))
                {
                    text.AppendLine();
                }
            }
        }

        /// <summary>The package's title, author and subject.</summary>
        public static (string? Title, string? Author, string? Subject) CoreProperties(OpenXmlPackage package)
        {
            var properties = package.PackageProperties;
            return (Blank(properties.Title), Blank(properties.Creator), Blank(properties.Subject));
        }

        private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
