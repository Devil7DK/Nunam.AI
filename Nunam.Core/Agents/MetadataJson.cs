using System.Text.Json.Nodes;

namespace Nunam.Core.Agents
{
    public static class MetadataJson
    {
        /// <summary>
        /// <paramref name="json"/> without NUL characters in its keys and strings: Postgres <c>jsonb</c>
        /// refuses <c>\u0000</c>, and document metadata (PDF titles stored as UTF-16, for one) can hold it.
        /// <c>null</c> for anything that isn't JSON.
        /// </summary>
        public static string? WithoutNul(string json)
        {
            if (!json.Contains("\\u0000", StringComparison.Ordinal)) return json;
            try
            {
                return Clean(JsonNode.Parse(json))?.ToJsonString();
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }

        private static JsonNode? Clean(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var cleaned = new JsonObject();
                    foreach (var (key, value) in obj.ToList())
                    {
                        obj.Remove(key);
                        cleaned[key.Replace("\0", "")] = Clean(value);
                    }
                    return cleaned;
                case JsonArray array:
                    var items = array.ToList();
                    array.Clear();
                    foreach (var item in items) array.Add(Clean(item));
                    return array;
                case JsonValue value when value.TryGetValue<string>(out var text):
                    return JsonValue.Create(text.Replace("\0", ""));
                default:
                    return node;
            }
        }
    }
}
