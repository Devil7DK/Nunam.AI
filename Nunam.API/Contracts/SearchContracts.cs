using Nunam.Core.Entities;

namespace Nunam.API.Contracts
{
    public enum SearchMode
    {
        Hybrid,
        Keyword,
        Semantic,
    }

    /// <summary>Query string of <c>GET /api/search</c>.</summary>
    /// <remarks><c>Mode</c> and <c>Type</c> are strings so they bind case-insensitively; see <see cref="SearchRequestParser"/>.</remarks>
    public record SearchRequest(
        string? Q,
        string? Mode,
        string? Type,
        string? Machine,
        string? Ext,
        DateOnly? From,
        DateOnly? To,
        bool? Described,
        int? Limit,
        int? Offset);

    /// <summary>A <see cref="SearchRequest"/> with its enum fields parsed.</summary>
    public record ParsedSearch(SearchRequest Request, SearchMode Mode, FileType? Type);

    public static class SearchRequestParser
    {
        /// <summary>Every word goes into the full-text query evaluated against each result, so cost grows with it.</summary>
        public const int MaxQueryChars = 500;
        public const int MaxQueryTerms = 50;

        public static ParsedSearch Parse(SearchRequest request, out Dictionary<string, string[]> errors)
        {
            errors = [];

            if (request.Q is { } q)
            {
                if (q.Length > MaxQueryChars)
                {
                    errors["q"] = [$"Search text is limited to {MaxQueryChars} characters."];
                }
                else if (q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() > MaxQueryTerms)
                {
                    errors["q"] = [$"Search text is limited to {MaxQueryTerms} words."];
                }
            }

            if (request.To == DateOnly.MaxValue)
            {
                errors["to"] = ["'to' is out of range."];
            }
            var mode = SearchMode.Hybrid;
            FileType? type = null;

            if (!string.IsNullOrWhiteSpace(request.Mode) && !EnumNames.TryParse(request.Mode, out mode))
            {
                errors["mode"] = [$"Unknown mode '{request.Mode}'. Use {Names<SearchMode>()}."];
            }

            if (!string.IsNullOrWhiteSpace(request.Type))
            {
                if (EnumNames.TryParse<FileType>(request.Type, out var parsed)) type = parsed;
                else errors["type"] = [$"Unknown type '{request.Type}'. Use {Names<FileType>()}."];
            }

            if (request.From is { } from && request.To is { } to && from > to)
            {
                errors["from"] = ["'from' must not be after 'to'."];
            }

            return new ParsedSearch(request, mode, type);
        }

        private static string Names<T>() where T : struct, Enum =>
            string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()));
    }

    public record SearchResponse(
        int Total,
        SearchMode Mode,
        bool SemanticUsed,
        string? Warning,
        IReadOnlyList<SearchResult> Results);

    /// <summary>
    /// <see cref="Snippet"/> marks matched words with U+E000 (start) and U+E001 (end), private-use
    /// characters that never occur in indexed text, so clients can highlight without parsing HTML.
    /// </summary>
    public record SearchResult(
        Guid Id,
        string FileName,
        string Path,
        string Machine,
        FileType FileType,
        string? Extension,
        long? SizeBytes,
        DateTimeOffset? ModifiedAt,
        string? Snippet,
        string? Description,
        string? DescriptionModel,
        bool HasPreview,
        double? Score,
        double? Similarity);
}
