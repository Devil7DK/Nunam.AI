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
        /// <summary>The pixel size an image declares in its header, or <c>null</c> when it can't be read.</summary>
        public static (int Width, int Height)? ReadSize(byte[] source)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(source));
            return codec is null ? null : (codec.Info.Width, codec.Info.Height);
        }

        /// <summary>
        /// Returns <c>null</c> when SkiaSharp can't decode the image (e.g. HEIC on Linux) or it declares
        /// more than <paramref name="maxPixels"/>: decoding allocates the full size before scaling, and a
        /// small, highly compressed file can declare gigabytes of pixels.
        /// </summary>
        public static PreviewImage? Create(byte[] source, int maxEdge, long maxPixels)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(source));
            if (codec is null || (long)codec.Info.Width * codec.Info.Height > maxPixels) return null;

            using var decoded = DecodeNear(codec, maxEdge);
            if (decoded is null) return null;

            var sideways = codec.EncodedOrigin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom;
            var (width, height) = sideways ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height);

            using var upright = ApplyOrigin(decoded, codec.EncodedOrigin);
            var image = upright ?? decoded;

            var scale = Math.Min(1.0, (double)maxEdge / Math.Max(image.Width, image.Height));
            using var resized = scale < 1.0
                ? image.Resize(
                    image.Info.WithSize((int)(image.Width * scale), (int)(image.Height * scale)),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                : null;

            var output = resized ?? image;
            var format = output.AlphaType == SKAlphaType.Opaque ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            using var data = output.Encode(format, 85);

            return data is null ? null : new PreviewImage(data.ToArray(), width, height);
        }

        /// <summary>
        /// Decodes at the smallest size the codec supports that is still at least <paramref name="maxEdge"/>
        /// (JPEG and WebP can; PNG decodes at full size), which keeps memory near the preview size.
        /// </summary>
        private static SKBitmap? DecodeNear(SKCodec codec, int maxEdge)
        {
            var scale = Math.Min(1f, (float)maxEdge / Math.Max(codec.Info.Width, codec.Info.Height));
            if (scale < 1f)
            {
                var size = codec.GetScaledDimensions(scale);
                if (size.Width < codec.Info.Width && Math.Max(size.Width, size.Height) >= maxEdge)
                {
                    var scaled = SKBitmap.Decode(codec, codec.Info.WithSize(size.Width, size.Height));
                    if (scaled is not null) return scaled;
                }
            }

            return SKBitmap.Decode(codec);
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
            var rotated = new SKBitmap(bitmap.Info.WithSize(
                swap ? bitmap.Height : bitmap.Width,
                swap ? bitmap.Width : bitmap.Height));
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
