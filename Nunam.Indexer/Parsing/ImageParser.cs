using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Reads EXIF (camera, date taken, GPS), OCRs any text in the image, and stores a scaled-down
    /// preview for the vision model. Images above the preview size cap get EXIF only.
    /// </summary>
    public class ImageParser(IOptions<IndexerOptions> options, OcrService ocr, ILogger<ImageParser> logger) : IFileParser
    {
        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) => fileType == FileType.Image;

        public async Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
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
                Content = ocr.ReadImage(bytes, file.FullName),
                Metadata = ToJson(exif, preview),
                PreviewImage = preview?.Bytes,
                NeedsDescribe = preview is not null,
            };
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

        private static string? ToJson(Exif? exif, PreviewImage? preview) => ParseMetadata.ToJson(
            ("width", preview?.Width),
            ("height", preview?.Height),
            ("cameraMake", exif?.Make),
            ("cameraModel", exif?.Model),
            ("takenAt", exif?.TakenAt?.ToString("s")),
            ("latitude", exif?.Latitude),
            ("longitude", exif?.Longitude));
    }
}
