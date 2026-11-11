using ClosedXML.Excel;

namespace Lms.E2ETests.Infrastructure;

/// <summary>Fills a downloaded import template and reads an error report (ClosedXML), for the import E2E.</summary>
public static class XlsxFiles
{
    /// <summary>One data row of the staff template: the column keys are the English header text of the template.</summary>
    public sealed record StaffRow(
        string NameAr, string NameEn, string Email, string Role, string UnitCode, string? Mobile = null, string? JobTitleEn = null);

    private static readonly Dictionary<string, string[]> Headers = new()
    {
        ["NameAr"] = ["الاسم بالعربية", "Name (Arabic)", "NameAr"],
        ["NameEn"] = ["الاسم بالإنجليزية", "Name (English)", "NameEn"],
        ["Email"] = ["البريد الإلكتروني", "Email"],
        ["Mobile"] = ["رقم الجوال", "Mobile"],
        ["JobTitleEn"] = ["المسمى الوظيفي بالإنجليزية", "Job title (English)", "JobTitleEn"],
        ["Role"] = ["الدور", "Role"],
        ["UnitCode"] = ["رمز الوحدة", "Unit code", "UnitCode"],
    };

    /// <summary>
    /// Writes the rows into the first worksheet of the template (header row 1, data from row 2, any example rows
    /// cleared first) and saves the result to <paramref name="outputPath"/>. Headers are found by their Arabic or
    /// English text, so either template language works.
    /// </summary>
    public static void FillStaffTemplate(string templatePath, string outputPath, IReadOnlyList<StaffRow> rows)
    {
        using var workbook = new XLWorkbook(templatePath);
        var sheet = workbook.Worksheet(1);
        var columns = ColumnIndexes(sheet);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (lastRow >= 2)
        {
            sheet.Rows(2, lastRow).Delete();
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var r = i + 2;
            Set(sheet, r, columns, "NameAr", row.NameAr);
            Set(sheet, r, columns, "NameEn", row.NameEn);
            Set(sheet, r, columns, "Email", row.Email);
            Set(sheet, r, columns, "Role", row.Role);
            Set(sheet, r, columns, "UnitCode", row.UnitCode);
            if (row.Mobile is not null)
            {
                Set(sheet, r, columns, "Mobile", row.Mobile);
            }

            if (row.JobTitleEn is not null)
            {
                Set(sheet, r, columns, "JobTitleEn", row.JobTitleEn);
            }
        }

        workbook.SaveAs(outputPath);
    }

    /// <summary>The error report: the header of its last column and, per worksheet row, the error text and cell fill colour.</summary>
    public sealed record ErrorReport(string LastHeader, IReadOnlyDictionary<int, (string Errors, string? Fill)> ErrorRows, int DataRows);

    public static ErrorReport ReadErrorReport(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        var lastColumn = sheet.LastColumnUsed()!.ColumnNumber();
        var header = sheet.Cell(1, lastColumn).GetString();
        var rows = new Dictionary<int, (string, string?)>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var r = 2; r <= lastRow; r++)
        {
            var text = sheet.Cell(r, lastColumn).GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                var fill = sheet.Row(r).Cells(1, lastColumn - 1)
                    .Select(c => c.Style.Fill.BackgroundColor)
                    .FirstOrDefault(color => color.ColorType == XLColorType.Color && color.Color.ToArgb() != 0 && color.Color.A != 0);
                rows[r] = (text, fill is null ? null : fill.Color.Name);
            }
        }

        return new ErrorReport(header, rows, lastRow - 1);
    }

    private static Dictionary<string, int> ColumnIndexes(IXLWorksheet sheet)
    {
        var result = new Dictionary<string, int>();
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            var text = cell.GetString().Trim();
            foreach (var (key, names) in Headers)
            {
                if (names.Any(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase)))
                {
                    result[key] = cell.Address.ColumnNumber;
                }
            }
        }

        foreach (var required in new[] { "NameAr", "NameEn", "Email", "Role", "UnitCode" })
        {
            if (!result.ContainsKey(required))
            {
                throw new InvalidOperationException($"The template has no '{required}' column; headers: {string.Join(" | ", sheet.Row(1).CellsUsed().Select(c => c.GetString()))}");
            }
        }

        return result;
    }

    private static void Set(IXLWorksheet sheet, int row, Dictionary<string, int> columns, string key, string value)
    {
        if (columns.TryGetValue(key, out var column))
        {
            sheet.Cell(row, column).SetValue(value); // text, so phone numbers and codes keep their form
        }
    }
}
