using Nunam.Core.Management;

namespace Nunam.Indexer.Management
{
    public static class RemoteSettingsExtensions
    {
        /// <summary>Overrides the local options with every setting the remote ones set.</summary>
        public static void ApplyTo(this RemoteIndexerSettings remote, IndexerOptions options)
        {
            if (remote.MachineName is { } name) options.MachineName = name.Trim();
            // A path that isn't absolute on this OS would resolve against the working directory.
            if (remote.WatchPaths is { } paths) options.WatchPaths = [.. paths.Where(Path.IsPathFullyQualified)];
            if (remote.ExcludePatterns is { } patterns) options.ExcludePatterns = patterns;
            if (remote.SkipSystemFolders is { } skip) options.SkipSystemFolders = skip;
            if (remote.MaxFileSizeMb is { } fileSize) options.MaxFileSizeMb = fileSize;
            if (remote.MaxPreviewMb is { } previewSize) options.MaxPreviewMb = previewSize;
            if (remote.EnableOcr is { } ocr) options.EnableOcr = ocr;
            if (remote.OcrLanguage is { } language) options.OcrLanguage = language;
            // Per extension: the machine's own lists for other extensions stay.
            if (remote.Parsers is { } parsers)
            {
                foreach (var (extension, names) in parsers) options.Parsers[extension] = names;
            }
            if (remote.OcrEngine is { } engine) options.OcrEngine = engine;
            if (remote.PdfRenderer is { } renderer) options.PdfRenderer = renderer;
        }

        /// <summary>The <see cref="IndexerOptions"/> property names the remote settings override.</summary>
        public static HashSet<string> OverriddenOptions(this RemoteIndexerSettings remote)
        {
            var set = new HashSet<string>();
            if (remote.MachineName is not null) set.Add(nameof(IndexerOptions.MachineName));
            if (remote.WatchPaths is not null) set.Add(nameof(IndexerOptions.WatchPaths));
            if (remote.ExcludePatterns is not null) set.Add(nameof(IndexerOptions.ExcludePatterns));
            if (remote.SkipSystemFolders is not null) set.Add(nameof(IndexerOptions.SkipSystemFolders));
            if (remote.MaxFileSizeMb is not null) set.Add(nameof(IndexerOptions.MaxFileSizeMb));
            if (remote.MaxPreviewMb is not null) set.Add(nameof(IndexerOptions.MaxPreviewMb));
            if (remote.EnableOcr is not null) set.Add(nameof(IndexerOptions.EnableOcr));
            if (remote.OcrLanguage is not null) set.Add(nameof(IndexerOptions.OcrLanguage));
            if (remote.Parsers is not null) set.Add(nameof(IndexerOptions.Parsers));
            if (remote.OcrEngine is not null) set.Add(nameof(IndexerOptions.OcrEngine));
            if (remote.PdfRenderer is not null) set.Add(nameof(IndexerOptions.PdfRenderer));
            return set;
        }
    }
}
