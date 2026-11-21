using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using ExcelDataReader.Exceptions;
using Microsoft.Extensions.Options;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;
using NumberFormat = ExcelNumberFormat.NumberFormat;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Extracts each worksheet's name and the displayed text of its cells, one row per line. Reads as a
    /// stream (.xls and .xlsx alike), so a huge sheet costs time rather than memory.
    /// </summary>
    public class ExcelParser(IOptions<IndexerOptions> options, ILogger<ExcelParser> logger) : IFileParser
    {
        public string Name => IndexerProviders.Parsers.ExcelDataReader;

        /// <summary>
        /// Cells visited, empty or not, before giving up on the rest of the file. A tiny file can declare a
        /// sheet of 17 billion cells, and the reader walks every row up to its last one.
        /// </summary>
        private const long MaxCellsVisited = 20_000_000;

        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".xls", ".xlsx", ".xlsm", ".xltx", ".xltm" };

        private static readonly ConcurrentDictionary<string, NumberFormat> Formats = new();

        private readonly IndexerOptions _options = options.Value;

        static ExcelParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        public bool CanParse(FileKind kind, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(ParseRequest request, CancellationToken cancellationToken)
        {
            var file = request.File;
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Task.FromResult(ParseOutcome.Empty);
            }

            using var stream = file.OpenRead();
            IExcelDataReader reader;
            try
            {
                reader = ExcelReaderFactory.CreateReader(stream);
            }
            catch (InvalidPasswordException)
            {
                logger.LogInformation("Skipping content of encrypted workbook {Path}", file.FullName);
                return Task.FromResult(new ParseOutcome { Metadata = ParseOutcome.MetadataOf(("encrypted", true)) });
            }

            var text = new StringBuilder();
            var sheetNames = new List<string>();
            long visited = 0;

            using (reader)
            {
                do
                {
                    sheetNames.Add(reader.Name);
                    text.AppendLine(reader.Name);

                    while (text.Length < _options.MaxContentChars && visited < MaxCellsVisited && reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        visited += Math.Max(1, reader.FieldCount);

                        var line = 0;
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            if (Display(reader, i) is not { } value) continue;
                            if (line++ > 0) text.Append('\t');
                            text.Append(value);
                        }
                        if (line > 0) text.AppendLine();
                    }
                }
                while (text.Length < _options.MaxContentChars && visited < MaxCellsVisited && reader.NextResult());
            }

            if (visited >= MaxCellsVisited)
            {
                logger.LogInformation("Stopped reading {Path} after {Cells} cells", file.FullName, visited);
            }

            return Task.FromResult(new ParseOutcome
            {
                Content = text.ToString().Trim(),
                Metadata = ParseOutcome.MetadataOf(("sheets", sheetNames)),
            });
        }

        /// <summary>The cell as Excel shows it: numbers and dates through the cell's number format.</summary>
        private static string? Display(IExcelDataReader reader, int column)
        {
            var value = reader.GetValue(column);
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
                case bool b:
                    return b ? "TRUE" : "FALSE";
            }

            var formatString = reader.GetNumberFormatString(column);
            if (!string.IsNullOrEmpty(formatString))
            {
                var format = Formats.GetOrAdd(formatString, f => new NumberFormat(f));
                if (format.IsValid)
                {
                    try
                    {
                        return format.Format(value, CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException)
                    {
                    }
                }
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
