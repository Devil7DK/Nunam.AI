using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Nunam.Core.Management
{
    /// <summary>
    /// The indexer settings that may be changed from the UI. A null field keeps the machine's own value.
    /// Settings that name a program, a plugin or a folder of native data (ffmpeg, commands, tessdata)
    /// stay local: whoever controls them controls what the indexer runs. Providers can only be chosen
    /// from <see cref="IndexerProviders"/>.
    /// </summary>
    public partial record RemoteIndexerSettings
    {
        public const int MaxWatchPaths = 50;
        public const int MaxExcludePatterns = 200;
        public const int MaxParserExtensions = 100;
        public const int MaxParsersPerExtension = 8;

        public string? MachineName { get; init; }
        public string[]? WatchPaths { get; init; }
        public string[]? ExcludePatterns { get; init; }
        public bool? SkipSystemFolders { get; init; }
        public int? MaxFileSizeMb { get; init; }
        public int? MaxPreviewMb { get; init; }
        public bool? EnableOcr { get; init; }
        public string? OcrLanguage { get; init; }

        /// <summary>Extension (<c>.pdf</c>) to the parsers to try for it, in order.</summary>
        public Dictionary<string, string[]>? Parsers { get; init; }
        public string? OcrEngine { get; init; }
        public string? PdfRenderer { get; init; }

        /// <summary>For this and the other management JSON stored in the database.</summary>
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static RemoteIndexerSettings FromJson(string json) =>
            JsonSerializer.Deserialize<RemoteIndexerSettings>(json, Json) ?? new RemoteIndexerSettings();

        /// <summary>Problems per field (camelCase), empty when the settings are usable.</summary>
        public Dictionary<string, string[]> Validate()
        {
            var errors = new Dictionary<string, string[]>();

            if (MachineName is not null && (MachineName.Trim().Length is 0 or > 100))
            {
                errors["machineName"] = ["Use 1 to 100 characters."];
            }

            if (WatchPaths is not null)
            {
                if (WatchPaths.Length > MaxWatchPaths) errors["watchPaths"] = [$"Use at most {MaxWatchPaths} watch paths."];
                else if (WatchPaths.FirstOrDefault(p => !IsFullPath(p)) is { } bad)
                {
                    errors["watchPaths"] = [$"\"{bad}\" isn't a full path, such as /home/me/Documents or C:\\Users\\me\\Documents."];
                }
            }

            if (ExcludePatterns is not null
                && (ExcludePatterns.Length > MaxExcludePatterns || ExcludePatterns.Any(p => p.Length is 0 or > 260 || p.Any(char.IsControl))))
            {
                errors["excludePatterns"] = [$"Use at most {MaxExcludePatterns} patterns of 1 to 260 characters."];
            }

            if (MaxFileSizeMb is < 1 or > 4096) errors["maxFileSizeMb"] = ["Use 1 to 4096 MB."];
            if (MaxPreviewMb is < 1 or > 512) errors["maxPreviewMb"] = ["Use 1 to 512 MB."];
            if (OcrLanguage is not null && !OcrLanguagePattern().IsMatch(OcrLanguage))
            {
                errors["ocrLanguage"] = ["Use Tesseract language codes joined with +, such as eng or eng+tam."];
            }

            if (Parsers is not null)
            {
                if (Parsers.Count > MaxParserExtensions) errors["parsers"] = [$"Use at most {MaxParserExtensions} extensions."];
                else if (Parsers.FirstOrDefault(p => !ExtensionPattern().IsMatch(p.Key)) is { Key: { } badExtension })
                {
                    errors["parsers"] = [$"\"{badExtension}\" isn't an extension, such as .pdf."];
                }
                else if (Parsers.FirstOrDefault(p => p.Value.Length is 0 or > MaxParsersPerExtension) is { Key: { } badCount })
                {
                    errors["parsers"] = [$"Give {badCount} 1 to {MaxParsersPerExtension} parsers."];
                }
                else if (Parsers.SelectMany(p => p.Value).FirstOrDefault(n => !IndexerProviders.Parsers.All.Contains(n)) is { } badParser)
                {
                    errors["parsers"] = [$"\"{badParser}\" isn't a built-in parser; commands and plugins are set on the machine. Use {string.Join(", ", IndexerProviders.Parsers.All)}."];
                }
            }
            if (OcrEngine is not null && !IndexerProviders.Ocr.All.Contains(OcrEngine))
            {
                errors["ocrEngine"] = [$"Use {string.Join(" or ", IndexerProviders.Ocr.All)}; commands and plugins are set on the machine."];
            }
            if (PdfRenderer is not null && !IndexerProviders.PdfRenderers.All.Contains(PdfRenderer))
            {
                errors["pdfRenderer"] = [$"Use {string.Join(" or ", IndexerProviders.PdfRenderers.All)}; plugins are set on the machine."];
            }

            return errors;
        }

        // The machine's OS isn't known here, so any absolute form passes: /…, C:\… or \\server\share\….
        private static bool IsFullPath(string path) =>
            path.Length is > 0 and <= 1024 && !path.Any(char.IsControl) && FullPathPattern().IsMatch(path);

        [GeneratedRegex(@"^(/|[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+)")]
        private static partial Regex FullPathPattern();

        [GeneratedRegex(@"^[a-z_]{3,20}(\+[a-z_]{3,20}){0,4}$")]
        private static partial Regex OcrLanguagePattern();

        [GeneratedRegex(@"^\.[a-z0-9]{1,16}$")]
        private static partial Regex ExtensionPattern();
    }
}
