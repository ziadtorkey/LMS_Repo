using System.Globalization;
using ClosedXML.Excel;
using Lms.Shared.Imports;

namespace Lms.Infrastructure.Imports;

/// <summary>Reads the first worksheet of an .xlsx: header row 1, data from row 2, blank rows skipped.</summary>
public static class ImportReader
{
    public const int MaxCellLength = 500;

    public static Task<ImportReadResult> ReadAsync(
        Stream xlsx, IReadOnlyList<ImportColumn> columns, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var workbook = new XLWorkbook(xlsx);
            return Task.FromResult(Read(workbook.Worksheets.First(), columns));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(new ImportReadResult([], [new RowError(0, null, "unreadable_file")]));
        }
    }

    private static ImportReadResult Read(IXLWorksheet sheet, IReadOnlyList<ImportColumn> columns)
    {
        var errors = new List<RowError>();
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var map = new Dictionary<int, ImportColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var c = 1; c <= lastColumn; c++)
        {
            var header = Normalize(sheet.Cell(1, c).GetFormattedString());
            if (header.Length == 0)
            {
                continue;
            }

            var column = columns.FirstOrDefault(col => Matches(col, header));
            if (column is null || !seen.Add(column.Key))
            {
                errors.Add(new RowError(0, null, "unknown_column",
                    new Dictionary<string, string> { ["column"] = sheet.Cell(1, c).GetFormattedString().Trim() }));
                continue;
            }

            map[c] = column;
        }

        foreach (var column in columns.Where(c => c.Required && !seen.Contains(c.Key)))
        {
            errors.Add(new RowError(0, null, "missing_column", new Dictionary<string, string> { ["column"] = column.HeaderEn }));
        }

        var rows = new List<ImportRowValues>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var column in columns)
            {
                values[column.Key] = null;
            }

            foreach (var (c, column) in map)
            {
                var text = CellText(sheet.Cell(r, c));
                values[column.Key] = text.Length == 0 ? null : text;
            }

            if (values.Values.All(v => v is null))
            {
                continue;
            }

            rows.Add(new ImportRowValues(r, values));
            if (rows.Count > ImportLimits.MaxRows)
            {
                errors.Add(new RowError(0, null, "too_many_rows",
                    new Dictionary<string, string> { ["max"] = ImportLimits.MaxRows.ToString(CultureInfo.InvariantCulture) }));
                return new ImportReadResult([], errors);
            }
        }

        if (rows.Count == 0 && errors.Count == 0)
        {
            errors.Add(new RowError(0, null, "empty_file"));
        }

        return new ImportReadResult(rows, errors);
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        // Dates come out as yyyy-MM-dd whatever the cell format; everything else as the text the user sees.
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return cell.GetFormattedString().Trim();
    }

    private static bool Matches(ImportColumn column, string header) =>
        header == Normalize(column.Key) || header == Normalize(column.HeaderEn) || header == Normalize(column.HeaderAr);

    private static string Normalize(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
}
