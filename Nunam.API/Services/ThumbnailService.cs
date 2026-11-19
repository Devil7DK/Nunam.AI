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
        public const int MinEdge = 32;
        public const int MaxEdge = 1568;

        /// <summary>
        /// <paramref name="version"/> changes whenever the file row does, so a re-rendered preview never
        /// serves a stale cached thumbnail.
        /// </summary>
        public byte[]? GetThumbnail(Guid fileId, long version, byte[] preview, int maxEdge)
        {
            var edge = Math.Clamp(maxEdge, MinEdge, MaxEdge);
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
            using var bitmap = SKBitmap.Decode(image);
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
