using ClosedXML.Excel;

namespace Lms.Infrastructure.Imports;

/// <summary>
/// The error report: every uploaded row in row-number order (so the file can be fixed and uploaded again) plus a trailing
/// Errors / الأخطاء column, errored cells filled #FFC7CE, messages in the culture of the batch.
/// </summary>
public static class ErrorReportWriter
{
    private static readonly XLColor ErrorFill = XLColor.FromHtml("#FFC7CE");

    public static void Write(
        IImportDefinition definition, IReadOnlyList<ImportRowValues> rows, IReadOnlyList<RowError> errors,
        string culture, IImportMessages messages, Stream output)
    {
        var arabic = culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        var columns = definition.Columns;
        for (var i = 0; i < columns.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = arabic ? columns[i].HeaderAr : columns[i].HeaderEn;
            if (columns[i].AsText)
            {
                sheet.Column(i + 1).Style.NumberFormat.Format = "@";
            }
        }

        var errorColumn = columns.Count + 1;
        sheet.Cell(1, errorColumn).Value = arabic ? "الأخطاء" : "Errors";
        sheet.Range(1, 1, 1, errorColumn).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.RightToLeft = arabic;

        var byRow = errors.Where(e => e.RowNumber > 0).ToLookup(e => e.RowNumber);
        var line = 2;

        foreach (var row in rows.OrderBy(r => r.RowNumber))
        {
            var messagesOfRow = new List<string>();
            for (var i = 0; i < columns.Count; i++)
            {
                sheet.Cell(line, i + 1).Value = row.Get(columns[i].Key) ?? string.Empty;
            }

            foreach (var error in byRow[row.RowNumber])
            {
                var text = messages.Format(error.Code, culture, error.Args);
                var columnIndex = error.ColumnKey is null ? -1 : columns.ToList().FindIndex(c => c.Key == error.ColumnKey);
                messagesOfRow.Add(columnIndex >= 0
                    ? $"{(arabic ? columns[columnIndex].HeaderAr : columns[columnIndex].HeaderEn)}: {text}"
                    : text);
                sheet.Cell(line, columnIndex >= 0 ? columnIndex + 1 : errorColumn).Style.Fill.BackgroundColor = ErrorFill;
            }

            if (messagesOfRow.Count > 0)
            {
                sheet.Cell(line, errorColumn).Value = string.Join("; ", messagesOfRow);
                sheet.Cell(line, errorColumn).Style.Fill.BackgroundColor = ErrorFill;
            }

            line++;
        }

        // File-level errors (missing column, too many rows) go below the data, in the Errors column.
        foreach (var error in errors.Where(e => e.RowNumber == 0))
        {
            sheet.Cell(line, errorColumn).Value = messages.Format(error.Code, culture, error.Args);
            sheet.Cell(line, errorColumn).Style.Fill.BackgroundColor = ErrorFill;
            line++;
        }

        sheet.Columns().AdjustToContents(1, line, 10, 60);
        workbook.SaveAs(output);
    }
}
