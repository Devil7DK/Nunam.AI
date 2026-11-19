namespace Nunam.Core.Files
{
    /// <summary>Maps file extensions to MIME types for image content sent to vision models.</summary>
    public static class MediaTypes
    {
        public static string FromExtension(string? extension) => extension?.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png",
        };
    }
}
