using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Nunam.API.Contracts;
using Nunam.Core.AI;
using Nunam.Core.Data;
using Nunam.Core.Entities;
using System.Text.RegularExpressions;
using Pgvector;

namespace Nunam.API.Services
{
    /// <summary>
    /// Keyword, semantic and hybrid search over <c>files</c>. Hybrid merges the keyword and semantic
    /// rankings with reciprocal rank fusion: <c>ts_rank</c> and cosine similarity live on unrelated
    /// scales, so adding them would let one side drown out the other, while ranks combine cleanly.
    /// </summary>
    public partial class SearchService(
        NunamDbContext db,
        IAIInferenceService ai,
        IOptions<SearchOptions> options,
        ILogger<SearchService> logger)
    {
        /// <summary>The usual RRF constant; it damps how much the very top ranks dominate.</summary>
        private const int RrfK = 60;

        private const int SnippetSourceChars = 100_000;
        private const string MatchStart = "\uE000";
        private const string MatchEnd = "\uE001";
        private readonly SearchOptions _options = options.Value;

        /// <param name="machineIds">Machines whose files may be returned; <c>null</c> for all.</param>
        public async Task<SearchResponse> SearchAsync(ParsedSearch search, Guid[]? machineIds, CancellationToken cancellationToken)
        {
            var (request, mode, type) = search;
            var query = request.Q?.Trim() ?? string.Empty;
            var limit = Math.Clamp(request.Limit ?? 20, 1, _options.MaxLimit);
            var offset = Math.Max(request.Offset ?? 0, 0);

            Vector? embedding = null;
            string? warning = null;
            if (query.Length > 0 && mode != SearchMode.Keyword)
            {
                (embedding, warning) = await TryEmbedAsync(query, cancellationToken);
                if (embedding is null && mode == SearchMode.Semantic)
                {
                    throw new SemanticSearchUnavailableException(
                        "The AI backend is unreachable, and semantic search needs it to read the query.");
                }
            }

            var useKeyword = query.Length > 0 && mode != SearchMode.Semantic;
            var useSemantic = embedding is not null;

            var parameters = FilterParameters(request, type, machineIds);
            parameters.AddRange(
            [
                new NpgsqlParameter("q", NpgsqlDbType.Text) { Value = query },
                new NpgsqlParameter("match_any", NpgsqlDbType.Boolean) { Value = mode == SearchMode.Hybrid },
                new NpgsqlParameter("semantic_margin", NpgsqlDbType.Double) { Value = _options.SemanticRelativeMargin },
                new NpgsqlParameter("name_pattern", NpgsqlDbType.Text) { Value = "%" + EscapeLike(query) + "%" },
                new NpgsqlParameter("use_keyword", NpgsqlDbType.Boolean) { Value = useKeyword },
                new NpgsqlParameter("use_semantic", NpgsqlDbType.Boolean) { Value = useSemantic },
                new NpgsqlParameter { ParameterName = "embedding", DataTypeName = "vector", Value = (object?)embedding ?? DBNull.Value },
                new NpgsqlParameter("candidates", NpgsqlDbType.Integer) { Value = _options.CandidatesPerSide },
                new NpgsqlParameter("rrf_k", NpgsqlDbType.Integer) { Value = RrfK },
                new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = limit },
                new NpgsqlParameter("offset", NpgsqlDbType.Integer) { Value = offset },
                new NpgsqlParameter("snippet_chars", NpgsqlDbType.Integer) { Value = SnippetSourceChars },
            ]);

            var sql = query.Length == 0 ? BrowseSql : SearchSql;

            // An HNSW scan returns at most hnsw.ef_search rows (default 40), fewer still when filters
            // drop some, so it is raised to the candidate count and allowed to keep scanning. SET LOCAL
            // keeps the change inside this transaction instead of leaking into the pooled connection.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (useSemantic)
            {
                // SET takes no bind parameters; the value is a clamped integer.
                var efSearch = Math.Clamp(_options.CandidatesPerSide, 40, 1000);
                var settings = FormattableString.Invariant(
                    $"SET LOCAL hnsw.ef_search = {efSearch}; SET LOCAL hnsw.iterative_scan = relaxed_order");
                await db.Database.ExecuteSqlRawAsync(settings, cancellationToken);
            }

            var rows = await db.Database
                .SqlQueryRaw<SearchRow>(sql, [.. parameters])
                .ToListAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var results = rows.Select(r => new SearchResult(
                r.Id, r.FileName, r.Path, r.Machine, Enum.Parse<FileType>(r.FileType), r.Extension, r.SizeBytes,
                r.FileModifiedAt, r.Snippet is null ? null : Whitespace().Replace(r.Snippet, " ").Trim(), r.Description, r.HasPreview,
                r.Score is null ? null : Math.Round(r.Score.Value, 4),
                r.Similarity is null ? null : Math.Round(r.Similarity.Value, 4))).ToList();

            return new SearchResponse((int)(rows.FirstOrDefault()?.Total ?? 0), mode, useSemantic, warning, results);
        }

        private async Task<(Vector? Embedding, string? Warning)> TryEmbedAsync(string query, CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.QueryEmbeddingTimeoutSeconds));

            try
            {
                return (await ai.EmbedQueryAsync(query, timeout.Token), null);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Could not embed the search query; semantic ranking skipped");
                return (null, "The AI backend is unreachable, so results are keyword matches only.");
            }
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        private static List<NpgsqlParameter> FilterParameters(SearchRequest request, FileType? type, Guid[]? machineIds)
        {
            var extensions = request.Ext?
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
                .ToArray();

            return
            [
                new NpgsqlParameter("file_type", NpgsqlDbType.Text) { Value = (object?)type?.ToString() ?? DBNull.Value },
                new NpgsqlParameter("machine", NpgsqlDbType.Text) { Value = (object?)request.Machine ?? DBNull.Value },
                new NpgsqlParameter("machine_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = (object?)machineIds ?? DBNull.Value },
                new NpgsqlParameter("extensions", NpgsqlDbType.Array | NpgsqlDbType.Text)
                {
                    Value = extensions is { Length: > 0 } ? extensions : DBNull.Value,
                },
                new NpgsqlParameter("from", NpgsqlDbType.TimestampTz)
                {
                    Value = request.From is { } from ? ToUtc(from) : DBNull.Value,
                },
                // "to" is a whole day, so the bound is the start of the next one.
                new NpgsqlParameter("to", NpgsqlDbType.TimestampTz)
                {
                    Value = request.To is { } to ? ToUtc(to.AddDays(1)) : DBNull.Value,
                },
            ];
        }

        private static DateTime ToUtc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        private static string EscapeLike(string value) =>
            value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

        private const string FilteredCte = """
            filtered AS (
                SELECT f.id
                FROM files f
                JOIN machines m ON m.id = f.machine_id
                WHERE (@file_type IS NULL OR f.file_type = @file_type)
                  AND (@machine_ids IS NULL OR f.machine_id = ANY(@machine_ids))
                  AND (@machine IS NULL OR lower(m.name) = lower(@machine))
                  AND (@extensions IS NULL OR lower(f.extension) = ANY(@extensions))
                  AND (@from IS NULL OR f.file_modified_at >= @from)
                  AND (@to IS NULL OR f.file_modified_at < @to)
            )
            """;

        private const string ResultColumns = """
                f.id, f.file_name, f.path, m.name AS machine, f.file_type, f.extension, f.size_bytes,
                f.file_modified_at, f.description, f.preview_image IS NOT NULL AS has_preview,
                p.score, p.similarity, p.total
            """;

        // Keyword mode keeps web-search syntax (all words, "phrases", -exclusions). Hybrid queries are
        // usually sentences, where requiring every word misses most relevant files, so there any word
        // matches and ts_rank_cd puts files matching more of them first.
        //
        // A filename hit ranks above body-only hits: the parser keeps "deed.txt" as one token, so
        // full-text search alone would miss a query for "deed".
        private static readonly string SearchSql = $"""
            WITH {FilteredCte},
            terms AS (
                SELECT CASE WHEN @match_any
                            THEN coalesce(nullif(replace(plainto_tsquery('english', @q)::text, ' & ', ' | '), '')::tsquery, plainto_tsquery('english', @q))
                            ELSE websearch_to_tsquery('english', @q)
                       END AS query
            ),
            keyword AS (
                SELECT f.id,
                       row_number() OVER (ORDER BY (f.file_name ILIKE @name_pattern) DESC, ts_rank_cd(f.fts, query) DESC) AS rank
                FROM files f
                CROSS JOIN terms
                WHERE @use_keyword
                  AND f.id IN (SELECT id FROM filtered)
                  AND (f.fts @@ query OR f.file_name ILIKE @name_pattern)
                ORDER BY rank
                LIMIT @candidates
            ),
            nearest AS (
                SELECT f.id, 1 - (f.embedding <=> @embedding) AS similarity
                FROM files f
                WHERE @use_semantic
                  AND f.embedding IS NOT NULL
                  AND f.id IN (SELECT id FROM filtered)
                ORDER BY f.embedding <=> @embedding
                LIMIT @candidates
            ),
            semantic AS (
                SELECT id, similarity, row_number() OVER (ORDER BY similarity DESC) AS rank
                FROM (SELECT id, similarity, max(similarity) OVER () AS best FROM nearest) n
                WHERE similarity >= best - @semantic_margin
            ),
            fused AS (
                SELECT coalesce(k.id, s.id) AS id,
                       coalesce(1.0 / (@rrf_k + k.rank), 0) + coalesce(1.0 / (@rrf_k + s.rank), 0) AS score,
                       s.similarity
                FROM keyword k
                FULL JOIN semantic s ON s.id = k.id
            ),
            page AS (
                SELECT id, score, similarity, count(*) OVER () AS total
                FROM fused
                ORDER BY score DESC, id
                LIMIT @limit OFFSET @offset
            )
            SELECT {ResultColumns},
                   CASE WHEN f.content IS NULL THEN NULL
                        ELSE ts_headline('english', left(f.content, @snippet_chars), (SELECT query FROM terms),
                             'StartSel="{MatchStart}", StopSel="{MatchEnd}", MaxWords=30, MinWords=12, MaxFragments=2, FragmentDelimiter=" … "')
                   END AS snippet
            FROM page p
            JOIN files f ON f.id = p.id
            JOIN machines m ON m.id = f.machine_id
            ORDER BY p.score DESC, f.id
            """;

        private static readonly string BrowseSql = $"""
            WITH {FilteredCte},
            page AS (
                SELECT f.id, NULL::double precision AS score, NULL::double precision AS similarity, count(*) OVER () AS total
                FROM files f
                WHERE f.id IN (SELECT id FROM filtered)
                ORDER BY f.file_modified_at DESC NULLS LAST, f.id
                LIMIT @limit OFFSET @offset
            )
            SELECT {ResultColumns}, left(f.content, 200) AS snippet
            FROM page p
            JOIN files f ON f.id = p.id
            JOIN machines m ON m.id = f.machine_id
            ORDER BY f.file_modified_at DESC NULLS LAST, f.id
            """;

        private class SearchRow
        {
            public Guid Id { get; set; }
            public string FileName { get; set; } = default!;
            public string Path { get; set; } = default!;
            public string Machine { get; set; } = default!;
            public string FileType { get; set; } = default!;
            public string? Extension { get; set; }
            public long? SizeBytes { get; set; }
            public DateTimeOffset? FileModifiedAt { get; set; }
            public string? Description { get; set; }
            public bool HasPreview { get; set; }
            public double? Score { get; set; }
            public double? Similarity { get; set; }
            public long Total { get; set; }
            public string? Snippet { get; set; }
        }
    }

    public class SemanticSearchUnavailableException(string message) : Exception(message);
}
