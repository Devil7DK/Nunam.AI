using System.Text;
using Microsoft.Extensions.Options;
using Nunam.Core.Entities;
using Syncfusion.XlsIO;

namespace Nunam.Indexer.Parsing
{
    /// <summary>Extracts each worksheet's name and the displayed text of its used cells, one row per line.</summary>
    public class ExcelParser(IOptions<IndexerOptions> options) : IFileParser
    {
        private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".xls", ".xlsx", ".xlsm", ".xltx", ".xltm" };

        private readonly IndexerOptions _options = options.Value;

        public bool CanParse(FileType fileType, string extension) => Extensions.Contains(extension);

        public Task<ParseOutcome> ParseAsync(FileInfo file, FileType fileType, CancellationToken cancellationToken)
        {
            if (file.Length > _options.MaxFileSizeBytes)
            {
                return Task.FromResult(ParseOutcome.Empty);
            }

            using var stream = file.OpenRead();
            using var engine = new ExcelEngine();
            var workbook = engine.Excel.Workbooks.Open(stream);

            var text = new StringBuilder();
            var sheetNames = new List<string>();

            foreach (IWorksheet sheet in workbook.Worksheets)
            {
                sheetNames.Add(sheet.Name);
                text.AppendLine(sheet.Name);

                foreach (var row in sheet.UsedRange.Rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var cells = row.Cells
                        .Select(c => c.DisplayText)
                        .Where(t => !string.IsNullOrWhiteSpace(t));
                    var line = string.Join('\t', cells);
                    if (line.Length > 0) text.AppendLine(line);

                    if (text.Length >= _options.MaxContentChars) break;
                }

                if (text.Length >= _options.MaxContentChars) break;
            }

            workbook.Close();

            return Task.FromResult(new ParseOutcome
            {
                Content = text.ToString(),
                Metadata = ParseMetadata.ToJson(("sheets", sheetNames)),
            });
        }
    }
}
