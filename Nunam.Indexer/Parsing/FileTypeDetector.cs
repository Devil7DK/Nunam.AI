using MimeDetective;
using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Classifies a file into a coarse <see cref="FileType"/>. Uses the extension first (cheap and
    /// usually right); for unknown extensions it falls back to magic-byte sniffing via MimeDetective.
    /// Registered as a singleton because building the content inspector is comparatively expensive.
    /// </summary>
    public class FileTypeDetector(ILogger<FileTypeDetector> logger)
    {
        private readonly IContentInspector _inspector = new ContentInspectorBuilder
        {
            Definitions = MimeDetective.Definitions.DefaultDefinitions.All(),
        }.Build();

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic" };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus" };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg" };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
            ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx", ".cs", ".java", ".py", ".go",
            ".rs", ".rb", ".php", ".c", ".cpp", ".h", ".hpp", ".sh", ".ps1", ".bat", ".sql",
            ".ini", ".cfg", ".conf", ".toml", ".pdf", ".doc", ".docx", ".xls", ".xlsx",
            ".ppt", ".pptx", ".rtf", ".odt", ".ods", ".odp",
        };

        public FileType Detect(FileInfo file)
        {
            var ext = file.Extension;

            if (ImageExtensions.Contains(ext)) return FileType.Image;
            if (AudioExtensions.Contains(ext)) return FileType.Audio;
            if (VideoExtensions.Contains(ext)) return FileType.Video;
            if (DocumentExtensions.Contains(ext)) return FileType.Document;

            return DetectByContent(file);
        }

        private FileType DetectByContent(FileInfo file)
        {
            try
            {
                var mime = _inspector.Inspect(file.FullName).ByMimeType().FirstOrDefault()?.MimeType;
                if (string.IsNullOrEmpty(mime)) return FileType.Other;

                if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return FileType.Image;
                if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return FileType.Audio;
                if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return FileType.Video;
                if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return FileType.Document;

                return mime switch
                {
                    "application/pdf" or "application/msword" or "application/rtf" => FileType.Document,
                    _ => FileType.Other,
                };
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Content sniffing failed for {Path}; classifying as Other", file.FullName);
                return FileType.Other;
            }
        }
    }
}
