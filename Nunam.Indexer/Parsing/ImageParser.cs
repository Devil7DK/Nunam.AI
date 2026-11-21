using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Reads EXIF (camera, date taken, GPS), OCRs any text in the image, and stores a scaled-down
    /// preview for the vision model. Images above the preview size cap get EXIF only.
    /// </summary>
    public class ImageParser(IOptions<IndexerOptions> options, ILogger<ImageParser> logger) : IFileParser
    {
        public string Name => IndexerProviders.Parsers.Image;

        private const int OcrEdgePx = 3000;
        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileKind kind, string extension) => kind == FileKind.Image;

        public async Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            var exif = ReadExif(file);

            if (file.Length > _options.MaxPreviewBytes)
            {
                logger.LogDebug("Skipping preview of oversized image {Path} ({Size} bytes)", file.FullName, file.Length);
                return new ParseOutcome { Metadata = ToJson(exif, null) };
            }

            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            if (PreviewImages.ReadSize(bytes) is { } size && (long)size.Width * size.Height > _options.MaxImagePixels)
            {
                logger.LogInformation("Skipping preview and OCR of {Path}: {Width}x{Height} is over the pixel limit",
                    file.FullName, size.Width, size.Height);
                return new ParseOutcome { Metadata = ToJson(exif, null) };
            }

            var preview = PreviewImages.Create(bytes, _options.PreviewMaxEdgePx, _options.MaxImagePixels);

            return new ParseOutcome
            {
                Content = await ReadTextAsync(bytes, request.Ocr, cancellationToken),
                Metadata = ToJson(exif, preview),
                PreviewImage = preview?.Bytes,
                NeedsDescribe = preview is not null,
            };
        }

        /// <summary>
        /// The OCR engine gets an upright copy no larger than <see cref="OcrEdgePx"/>, which also covers formats
        /// it can't decode itself.
        /// </summary>
        private async Task<string?> ReadTextAsync(byte[] bytes, IOcrEngine ocr, CancellationToken cancellationToken)
        {
            if (!ocr.IsAvailable) return null;

            var image = PreviewImages.Create(bytes, OcrEdgePx, _options.MaxImagePixels);
            return image is null ? null : await ocr.ReadImageAsync(image.Bytes, cancellationToken);
        }

        private record Exif(string? Make, string? Model, DateTime? TakenAt, double? Latitude, double? Longitude);

        private Exif? ReadExif(FileInfo file)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(file.FullName);
                var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var location = directories.OfType<GpsDirectory>().FirstOrDefault()?.GetGeoLocation();

                DateTime? takenAt = subIfd is not null && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken)
                    ? taken
                    : null;

                return new Exif(
                    ifd0?.GetDescription(ExifDirectoryBase.TagMake)?.Trim(),
                    ifd0?.GetDescription(ExifDirectoryBase.TagModel)?.Trim(),
                    takenAt,
                    location?.Latitude,
                    location?.Longitude);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "No readable metadata in {Path}", file.FullName);
                return null;
            }
        }

        private static IReadOnlyDictionary<string, object?>? ToJson(Exif? exif, PreviewImage? preview) => ParseOutcome.MetadataOf(
            ("width", preview?.Width),
            ("height", preview?.Height),
            ("cameraMake", exif?.Make),
            ("cameraModel", exif?.Model),
            ("takenAt", exif?.TakenAt?.ToString("s")),
            ("latitude", exif?.Latitude),
            ("longitude", exif?.Longitude));
    }
}
