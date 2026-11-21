namespace Nunam.Core.Management
{
    /// <summary>
    /// Names of the parsers, OCR engines and PDF renderers built into the indexer. Only these can be
    /// chosen from the UI; external commands and plugins are named in the machine's own settings.
    /// </summary>
    public static class IndexerProviders
    {
        public const string None = "none";

        public static class Parsers
        {
            public const string Text = "text";
            public const string PdfPig = "pdfpig";
            public const string OpenXmlWord = "openxml-word";
            public const string Word97 = "word97";
            public const string Rtf = "rtf";
            public const string ExcelDataReader = "exceldatareader";
            public const string OpenXmlPowerPoint = "openxml-powerpoint";
            public const string Image = "image";
            public const string TagLib = "taglib";
            public const string FFmpeg = "ffmpeg";

            /// <summary>Indexes name and file details only; always tried last.</summary>
            public const string Fallback = "fallback";

            public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { Text, PdfPig, OpenXmlWord, Word97, Rtf, ExcelDataReader, OpenXmlPowerPoint, Image, TagLib, FFmpeg, Fallback };
        }

        public static class Ocr
        {
            public const string Tesseract = "tesseract";

            public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Tesseract, None };
        }

        public static class PdfRenderers
        {
            public const string Pdfium = "pdfium";

            public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Pdfium, None };
        }

        /// <summary>Prefix that names an external command from <c>Indexer:Commands</c>, as in <c>command:pandoc</c>.</summary>
        public const string CommandPrefix = "command:";
    }
}
