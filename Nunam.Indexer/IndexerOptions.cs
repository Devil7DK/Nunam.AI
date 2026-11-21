namespace Nunam.Indexer
{
    /// <summary>
    /// Configuration for the file-indexing host: which machine this is, which paths to crawl,
    /// what to skip, and the size limits / queue pacing for the parse pipeline.
    /// </summary>
    public class IndexerOptions
    {
        public const string SectionName = "Indexer";

        /// <summary>
        /// Lets admins and the machine's owners change some settings and restart, pause or resume the
        /// indexer from the UI. Only this machine's own settings can turn it on.
        /// </summary>
        public bool RemoteManagement { get; set; }

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

        /// <summary>
        /// Also skip the OS, installed programs, caches and app data inside a watch root
        /// (<see cref="Indexing.SystemFolders"/>), so a whole drive can be a watch path.
        /// </summary>
        public bool SkipSystemFolders { get; set; } = true;

        /// <summary>Files larger than this are indexed for metadata only — their content is not read.</summary>
        public int MaxFileSizeMb { get; set; } = 32;

        /// <summary>Images larger than this get metadata only: no OCR, no preview, no describe.</summary>
        public int MaxPreviewMb { get; set; } = 8;

        /// <summary>Previews are scaled down so their longer edge is at most this many pixels.</summary>
        public int PreviewMaxEdgePx { get; set; } = 1568;

        /// <summary>
        /// Images declaring more pixels than this get metadata only. Decoding allocates width × height ×
        /// 4 bytes, and a file of a few MB can declare far more than that.
        /// </summary>
        public int MaxImageMegapixels { get; set; } = 100;

        /// <summary>A single file's parse is abandoned (job failed) after this long.</summary>
        public int ParseTimeoutSeconds { get; set; } = 300;

        /// <summary>Run OCR on images and on PDFs that have no text layer.</summary>
        public bool EnableOcr { get; set; } = true;

        /// <summary>
        /// Tesseract language code(s), e.g. <c>eng</c> or <c>eng+tam</c>. Each needs its
        /// <c>.traineddata</c> file in Tesseract's tessdata folder.
        /// </summary>
        public string OcrLanguage { get; set; } = "eng";

        /// <summary>
        /// The <c>tesseract</c> executable; when unset it's looked up on PATH, and on Windows also in
        /// <c>Program Files\Tesseract-OCR</c>.
        /// </summary>
        public string? OcrTesseractPath { get; set; }

        /// <summary>Tessdata folder, when not the one Tesseract was installed with.</summary>
        public string? OcrTessDataPath { get; set; }

        /// <summary>Scanned PDFs are OCRed up to this many pages.</summary>
        public int OcrMaxPdfPages { get; set; } = 20;

        /// <summary>Folder holding <c>ffmpeg</c>/<c>ffprobe</c>; when unset they're looked up on PATH.</summary>
        public string? FFmpegBinaryFolder { get; set; }

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

        /// <summary>Claims per job before a job that keeps stopping the worker is failed.</summary>
        public int MaxAttempts { get; set; } = 3;

        public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
        public long MaxPreviewBytes => MaxPreviewMb * 1024L * 1024L;
        public long MaxImagePixels => MaxImageMegapixels * 1_000_000L;
    }
}
