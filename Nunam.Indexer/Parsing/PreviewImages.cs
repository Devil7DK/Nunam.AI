using SkiaSharp;

namespace Nunam.Indexer.Parsing
{
    /// <summary>A preview ready to store, plus the source image's pixel size.</summary>
    public record PreviewImage(byte[] Bytes, int Width, int Height);

    /// <summary>
    /// Turns source images into previews for the vision model: upright, no longer than the configured
    /// edge, JPEG unless the image has transparency.
    /// </summary>
    public static class PreviewImages
    {
        /// <summary>Returns <c>null</c> when SkiaSharp can't decode the image (e.g. HEIC on Linux).</summary>
        public static PreviewImage? Create(byte[] source, int maxEdge)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(source));
            if (codec is null) return null;

            using var decoded = SKBitmap.Decode(codec);
            if (decoded is null) return null;

            using var upright = ApplyOrigin(decoded, codec.EncodedOrigin);
            var image = upright ?? decoded;
            var (width, height) = (image.Width, image.Height);

            var scale = Math.Min(1.0, (double)maxEdge / Math.Max(width, height));
            using var resized = scale < 1.0
                ? image.Resize(
                    new SKImageInfo((int)(width * scale), (int)(height * scale)),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                : null;

            var output = resized ?? image;
            var format = output.AlphaType == SKAlphaType.Opaque ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            using var data = output.Encode(format, 85);

            return data is null ? null : new PreviewImage(data.ToArray(), width, height);
        }

        /// <summary>Phones store photos sideways and record the rotation in EXIF; mirrored origins are left as-is.</summary>
        private static SKBitmap? ApplyOrigin(SKBitmap bitmap, SKEncodedOrigin origin)
        {
            var degrees = origin switch
            {
                SKEncodedOrigin.BottomRight => 180,
                SKEncodedOrigin.RightTop => 90,
                SKEncodedOrigin.LeftBottom => 270,
                _ => 0,
            };
            if (degrees == 0) return null;

            var swap = degrees != 180;
            var rotated = new SKBitmap(swap ? bitmap.Height : bitmap.Width, swap ? bitmap.Width : bitmap.Height);
            using var canvas = new SKCanvas(rotated);
            canvas.Translate(rotated.Width / 2f, rotated.Height / 2f);
            canvas.RotateDegrees(degrees);
            canvas.Translate(-bitmap.Width / 2f, -bitmap.Height / 2f);
            using var source = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(source, 0, 0, SKSamplingOptions.Default);
            return rotated;
        }
    }
}
