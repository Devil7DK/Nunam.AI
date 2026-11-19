using System.Text.Json;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Builds the <c>metadata</c> JSON blob from the fields a parser found, dropping empty ones.</summary>
    public static class ParseMetadata
    {
        public static string? ToJson(params (string Key, object? Value)[] fields)
        {
            var present = fields
                .Where(f => f.Value is not null && f.Value is not string { Length: 0 })
                .ToDictionary(f => f.Key, f => f.Value);

            return present.Count == 0 ? null : JsonSerializer.Serialize(present);
        }
    }
}
