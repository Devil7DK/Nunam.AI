using System.Text;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;
using Syncfusion.Presentation;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts slide text — shapes, groups, tables and speaker notes — from .pptx-family files.
    /// Legacy binary .ppt is not supported by Syncfusion.Presentation and falls through to the fallback parser.
    /// </summary>
    public class PowerPointParser(IOptions<IndexerOptions> options) : IFileParser
    {
        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm" };

        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Task.FromResult(ParseOutcome.Empty);
            }

            using var stream = file.OpenRead();
            using var presentation = Presentation.Open(stream);

            var text = new StringBuilder();
            foreach (ISlide slide in presentation.Slides)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AppendShapes(text, slide.Shapes);
                if (slide.NotesSlide?.NotesTextBody?.Text is { Length: > 0 } notes)
                {
                    text.AppendLine(notes);
                }

                if (text.Length >= _options.MaxContentChars) break;
            }

            return Task.FromResult(new ParseOutcome
            {
                Content = text.ToString(),
                Metadata = ParseMetadata.ToJson(
                    ("slides", presentation.Slides.Count),
                    ("title", presentation.BuiltInDocumentProperties.Title),
                    ("author", presentation.BuiltInDocumentProperties.Author)),
            });
        }

        private static void AppendShapes(StringBuilder text, IShapes shapes)
        {
            foreach (ISlideItem item in shapes)
            {
                switch (item)
                {
                    case IGroupShape group:
                        AppendShapes(text, group.Shapes);
                        break;
                    case ITable table:
                        foreach (IRow row in table.Rows)
                        {
                            text.AppendLine(string.Join('\t', row.Cells.Select(c => c.TextBody?.Text)));
                        }
                        break;
                    case IShape shape when shape.TextBody?.Text is { Length: > 0 } body:
                        text.AppendLine(body);
                        break;
                }
            }
        }
    }
}
