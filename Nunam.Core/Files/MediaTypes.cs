namespace Nunam.Core.Files
{
    /// <summary>Identifies the MIME type of image content sent to vision models.</summary>
    public static class MediaTypes
    {
        /// <summary>
        /// Reads the type from the image's magic bytes, falling back to the file extension. A preview
        /// rendered from a PDF is a PNG even though the file's extension is <c>.pdf</c>.
        /// </summary>
        public static string Detect(ReadOnlySpan<byte> image, string? extension) => image switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
            [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
            [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
            [0x42, 0x4D, ..] => "image/bmp",
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
            [0x49, 0x49, 0x2A, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A, ..] => "image/tiff",
            _ => FromExtension(extension),
        };

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
