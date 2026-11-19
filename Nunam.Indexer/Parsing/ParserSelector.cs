using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Picks the first registered <see cref="IFileParser"/> that can handle a file. DI registration
    /// order is significant: specialized parsers first, <see cref="FallbackParser"/> last so it only
    /// catches what nothing else claims.
    /// </summary>
    public class ParserSelector(IEnumerable<IFileParser> parsers)
    {
        private readonly IReadOnlyList<IFileParser> _parsers = [.. parsers];

        public IFileParser Select(FileType fileType, string extension) =>
            _parsers.First(p => p.CanParse(fileType, extension));
    }
}
