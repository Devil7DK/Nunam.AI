using Microsoft.Extensions.Options;
using Nunam.Core.Entities;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Stores image bytes as the file's preview and flags it for a vision describe pass. Images above
    /// the preview size cap are indexed for metadata only (no preview, no describe).
    /// </summary>
    public class ImageParser(IOptions<IndexerOptions> options, ILogger<ImageParser> logger) : IFileParser
    {
        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) => fileType == FileType.Image;

        public async Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxPreviewBytes)
            {
                logger.LogDebug("Skipping preview of oversized image {Path} ({Size} bytes)", file.FullName, file.Length);
                return ParseOutcome.Empty;
            }

            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            return new ParseOutcome { PreviewImage = bytes, NeedsDescribe = true };
        }
    }
}
