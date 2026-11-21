using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts slide text (shapes, groups, tables) and speaker notes, in slide order, from .pptx-family
    /// files. Legacy binary .ppt falls through to the fallback parser.
    /// </summary>
    public class PowerPointParser(IOptions<IndexerOptions> options) : IFileParser
    {
        public string Name => IndexerProviders.Parsers.OpenXmlPowerPoint;

        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm" };

        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileKind kind, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Task.FromResult(ParseOutcome.Empty);
            }

            using var presentation = PresentationDocument.Open(file.FullName, false, OfficeText.Settings);
            var main = presentation.PresentationPart;
            var slideIds = main?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList() ?? [];

            var text = new StringBuilder();
            foreach (var slideId in slideIds)
            {
                if (text.Length >= _options.MaxContentChars) break;
                if (slideId.RelationshipId?.Value is not { } id || main!.GetPartById(id) is not SlidePart slide) continue;

                OfficeText.Append(text, slide, _options.MaxContentChars, cancellationToken);
                if (slide.NotesSlidePart is { } notes)
                {
                    OfficeText.Append(text, notes, _options.MaxContentChars, cancellationToken);
                }
            }

            var (title, author, _) = OfficeText.CoreProperties(presentation);
            return Task.FromResult(new ParseOutcome
            {
                Content = text.ToString().Trim(),
                Metadata = ParseOutcome.MetadataOf(("slides", slideIds.Count), ("title", title), ("author", author)),
            });
        }
    }
}
