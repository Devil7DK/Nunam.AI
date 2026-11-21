using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Reads tags (title, artists, album, genre, comment, lyrics) and stream properties. The tag text
    /// becomes content so a song is found by what it is called, not only by its filename.
    /// </summary>
    public class AudioParser(ILogger<AudioParser> logger) : IFileParser
    {
        public string Name => IndexerProviders.Parsers.TagLib;

        public bool CanParse(FileKind kind, string extension) => kind == FileKind.Audio;

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            TagLib.File media;
            try
            {
                media = TagLib.File.Create(file.FullName);
            }
            catch (Exception ex) when (ex is TagLib.UnsupportedFormatException or TagLib.CorruptFileException)
            {
                logger.LogDebug(ex, "No readable tags in {Path}", file.FullName);
                return Task.FromResult(ParseOutcome.Empty);
            }

            using (media)
            {
                var tag = media.Tag;
                var properties = media.Properties;
                var artists = tag.Performers.Concat(tag.AlbumArtists).Distinct().ToArray();

                var content = string.Join('\n', new[]
                {
                    tag.Title, string.Join(", ", artists), tag.Album, string.Join(", ", tag.Genres), tag.Comment, tag.Lyrics,
                }.Where(t => !string.IsNullOrWhiteSpace(t)));

                return Task.FromResult(new ParseOutcome
                {
                    Content = content,
                    Metadata = ParseOutcome.MetadataOf(
                        ("title", tag.Title),
                        ("artists", artists.Length > 0 ? artists : null),
                        ("album", tag.Album),
                        ("year", tag.Year > 0 ? tag.Year : null),
                        ("track", tag.Track > 0 ? tag.Track : null),
                        ("genres", tag.Genres.Length > 0 ? tag.Genres : null),
                        ("durationSeconds", properties is null ? null : Math.Round(properties.Duration.TotalSeconds, 1)),
                        ("bitrateKbps", properties?.AudioBitrate > 0 ? properties.AudioBitrate : null),
                        ("sampleRate", properties?.AudioSampleRate > 0 ? properties.AudioSampleRate : null),
                        ("channels", properties?.AudioChannels > 0 ? properties.AudioChannels : null)),
                });
            }
        }
    }
}
