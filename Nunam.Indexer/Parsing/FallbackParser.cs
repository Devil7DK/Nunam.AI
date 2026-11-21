using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Catch-all parser for files no specialized parser handles (audio, video, office docs, binaries).
    /// Extracts nothing — the file is still indexed for its filename and metadata, which remain
    /// searchable and embeddable. Must be registered last so it only runs when nothing else matches.
    /// </summary>
    public class FallbackParser : IFileParser
    {
        public string Name => IndexerProviders.Parsers.Fallback;

        public bool CanParse(FileKind kind, string extension) => true;

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(ParseOutcome.Empty);
    }
}
