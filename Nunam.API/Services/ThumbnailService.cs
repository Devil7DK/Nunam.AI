using Microsoft.Extensions.Caching.Memory;
using SkiaSharp;

namespace Nunam.API.Services
{
    /// <summary>
    /// Scales stored previews down to thumbnail size on request and keeps the results in memory, so a
    /// page of search results doesn't ship full 1568 px previews.
    /// </summary>
    public class ThumbnailService(IMemoryCache cache)
    {
        public const int MaxEdge = 1568;

        /// <summary>Requested sizes round up to one of these, so a file has a handful of cache entries, not one per pixel.</summary>
        private static readonly int[] Edges = [128, 256, 512, 1024, MaxEdge];

        /// <summary>
        /// Previews are stored at most <see cref="MaxEdge"/> wide; anything far larger is refused rather than
        /// decoded, since decoding allocates width × height × 4 bytes in this shared process.
        /// </summary>
        private const long MaxSourcePixels = 4096L * 4096;

        /// <summary>
        /// <paramref name="version"/> changes whenever the file row does, so a re-rendered preview never
        /// serves a stale cached thumbnail.
        /// </summary>
        public byte[]? GetThumbnail(Guid fileId, long version, byte[] preview, int maxEdge)
        {
            var edge = Edges.FirstOrDefault(e => e >= maxEdge, MaxEdge);
            return cache.GetOrCreate((fileId, version, edge), entry =>
            {
                var thumbnail = Resize(preview, edge);
                entry.Size = thumbnail?.Length ?? 1;
                entry.SlidingExpiration = TimeSpan.FromHours(1);
                return thumbnail;
            });
        }

        private static byte[]? Resize(byte[] image, int maxEdge)
        {
            using var codec = SKCodec.Create(new SKMemoryStream(image));
            if (codec is null || (long)codec.Info.Width * codec.Info.Height > MaxSourcePixels) return null;

            // JPEG can decode straight at a fraction of its size; other formats decode in full.
            var target = codec.GetScaledDimensions(Math.Min(1f, (float)maxEdge / Math.Max(codec.Info.Width, codec.Info.Height)));
            using var bitmap = (Math.Max(target.Width, target.Height) >= maxEdge && target.Width < codec.Info.Width
                    ? SKBitmap.Decode(codec, codec.Info.WithSize(target.Width, target.Height))
                    : null)
                ?? SKBitmap.Decode(codec);
            if (bitmap is null) return null;

            var scale = (double)maxEdge / Math.Max(bitmap.Width, bitmap.Height);
            if (scale >= 1.0) return image;

            using var resized = bitmap.Resize(
                bitmap.Info.WithSize(Math.Max(1, (int)(bitmap.Width * scale)), Math.Max(1, (int)(bitmap.Height * scale))),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            var format = resized.AlphaType == SKAlphaType.Opaque ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;
            using var data = resized.Encode(format, 80);
            return data?.ToArray();
        }
    }
}
