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

                // Populated cells only: UsedRange spans the bounding box, which two far-apart cells in a
                // tiny file can stretch to billions of empty cells.
                var row = -1;
                foreach (var cell in sheet.UsedCells)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var value = cell.DisplayText;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    if (cell.Row != row)
                    {
                        if (row != -1) text.AppendLine();
                        row = cell.Row;
                    }
                    else
                    {
                        text.Append('\t');
                    }
                    text.Append(value);

                    if (text.Length >= _options.MaxContentChars) break;
                }
                if (row != -1) text.AppendLine();

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
