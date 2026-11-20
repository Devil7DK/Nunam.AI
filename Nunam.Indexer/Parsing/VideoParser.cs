using System.Globalization;
using FFMpegCore;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Probes duration, resolution and codecs with ffprobe and grabs one frame with ffmpeg as the
    /// preview for the vision model. Needs ffmpeg installed; without it videos are indexed by name only.
    /// </summary>
    public class VideoParser(IOptions<IndexerOptions> options, ILogger<VideoParser> logger) : IFileParser
    {
        private static readonly TimeSpan MaxFrameOffset = TimeSpan.FromSeconds(30);

        /// <summary>
        /// A file named .mp4 can hold an HLS playlist whose entries make ffmpeg open other local files or
        /// URLs. Only plain files, read by the container demuxers real videos use, are allowed.
        /// </summary>
        private const string InputGuard =
            "-protocol_whitelist file -format_whitelist mov,matroska,webm,avi,asf,flv,mpeg,mpegts,mpegvideo,ogg";
        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) => fileType == FileType.Video;

        public async Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            var ffOptions = new FFOptions { BinaryFolder = _options.FFmpegBinaryFolder ?? string.Empty };

            IMediaAnalysis analysis;
            try
            {
                analysis = await FFProbe.AnalyseAsync(file.FullName, ffOptions, InputGuard, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "ffprobe could not read {Path}; indexing by name only", file.FullName);
                return ParseOutcome.Empty;
            }

            var video = analysis.PrimaryVideoStream;
            var tags = analysis.Format.Tags;
            var title = tags?.GetValueOrDefault("title");
            var comment = tags?.GetValueOrDefault("comment");

            var preview = video is null ? null : await GrabFrameAsync(file, analysis, ffOptions, cancellationToken);

            return new ParseOutcome
            {
                Content = string.Join('\n', new[] { title, comment }.Where(t => !string.IsNullOrWhiteSpace(t))),
                Metadata = ParseMetadata.ToJson(
                    ("durationSeconds", Math.Round(analysis.Duration.TotalSeconds, 1)),
                    ("width", video?.Width),
                    ("height", video?.Height),
                    ("frameRate", video is null ? null : Math.Round(video.AverageFrameRate, 2)),
                    ("videoCodec", video?.CodecName),
                    ("audioCodec", analysis.PrimaryAudioStream?.CodecName),
                    ("container", analysis.Format.FormatName),
                    ("title", title),
                    ("createdAt", tags?.GetValueOrDefault("creation_time"))),
                PreviewImage = preview,
                NeedsDescribe = preview is not null,
            };
        }

        /// <summary>A frame a tenth of the way in (at most 30 s) skips the black or title frames most videos open with.</summary>
        private async Task<byte[]?> GrabFrameAsync(
            FileInfo file, IMediaAnalysis analysis, FFOptions ffOptions, CancellationToken cancellationToken)
        {
            var at = TimeSpan.FromTicks(Math.Min(analysis.Duration.Ticks / 10, MaxFrameOffset.Ticks));
            var framePath = Path.Combine(Path.GetTempPath(), $"nunam-frame-{Guid.NewGuid():N}.png");

            try
            {
                await FFMpegArguments
                    .FromFileInput(file.FullName, verifyExists: true, input => input.WithCustomArgument(
                        $"{InputGuard} -ss {at.TotalSeconds.ToString(CultureInfo.InvariantCulture)}"))
                    .OutputToFile(framePath, overwrite: true, output => output.WithCustomArgument("-frames:v 1 -c:v png"))
                    .CancellableThrough(cancellationToken)
                    .ProcessAsynchronously(true, ffOptions);

                var frame = await File.ReadAllBytesAsync(framePath, cancellationToken);
                return PreviewImages.Create(frame, _options.PreviewMaxEdgePx, _options.MaxImagePixels)?.Bytes;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not grab a frame from {Path}; indexing metadata only", file.FullName);
                return null;
            }
            finally
            {
                File.Delete(framePath);
            }
        }
    }
}
