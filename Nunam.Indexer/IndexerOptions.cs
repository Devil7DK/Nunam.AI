namespace Nunam.Indexer
{
    /// <summary>
    /// Configuration for the file-indexing host: which machine this is, which paths to crawl,
    /// what to skip, and the size limits / queue pacing for the parse pipeline.
    /// </summary>
    public class IndexerOptions
    {
        public const string SectionName = "Indexer";

        /// <summary>Friendly machine name. Falls back to the OS hostname when blank.</summary>
        public string? MachineName { get; set; }

        /// <summary>Root directories to scan and watch (recursively).</summary>
        public string[] WatchPaths { get; set; } = [];

        /// <summary>
        /// Case-insensitive substring/wildcard patterns; any path containing a match (or matching a
        /// <c>*</c> glob) is skipped. Write separators as <c>/</c> — both <c>/</c> and <c>\</c> match on
        /// every OS. Defaults cover noisy build/VCS/system folders.
        /// </summary>
        public string[] ExcludePatterns { get; set; } =
        [
            "/.git/", "/node_modules/", "/bin/", "/obj/", "/.vs/",
            "/$RECYCLE.BIN/", "/System Volume Information/", "*.tmp",
        ];

        /// <summary>Files larger than this are indexed for metadata only — their content is not read.</summary>
        public int MaxFileSizeMb { get; set; } = 32;

        /// <summary>Images larger than this are not stored as a preview (so not sent to the vision model).</summary>
        public int MaxPreviewMb { get; set; } = 8;

        /// <summary>
        /// Extracted content is cut to this many characters. Postgres refuses a tsvector over 1 MB,
        /// which the full-text trigger builds from this column.
        /// </summary>
        public int MaxContentChars { get; set; } = 500_000;

        /// <summary>Parse jobs claimed per poll.</summary>
        public int ParseBatchSize { get; set; } = 8;

        /// <summary>Delay between polls when the Parse queue is empty.</summary>
        public int IdlePollSeconds { get; set; } = 5;

        /// <summary>First backoff delay after a transient failure (e.g. database unreachable).</summary>
        public int MinBackoffSeconds { get; set; } = 5;

        /// <summary>Backoff ceiling.</summary>
        public int MaxBackoffSeconds { get; set; } = 120;

        /// <summary>A Parse job left in <c>Processing</c> longer than this is assumed abandoned and requeued.</summary>
        public int StaleJobMinutes { get; set; } = 30;

        public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
        public long MaxPreviewBytes => MaxPreviewMb * 1024L * 1024L;
    }
}
