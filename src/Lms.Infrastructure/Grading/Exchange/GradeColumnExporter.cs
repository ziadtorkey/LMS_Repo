using ClosedXML.Excel;
using Lms.Infrastructure.Spreadsheets;

namespace Lms.Infrastructure.Grading.Exchange;

/// <summary>One line of an item column export.</summary>
public sealed record ColumnExportRow(string StudentNumber, string NameEn, string NameAr, decimal? Points);

/// <summary>
/// The column of one grade item as an <c>.xlsx</c>, keyed by student number (M5 Task 10, L9): the sheet a teacher fills in and uploads again. Every text
/// cell goes through <see cref="SpreadsheetCell.Safe"/> (formula injection), points are numbers.
/// </summary>
public static class GradeColumnExporter
{
    public const string StudentNumberHeader = "StudentNumber";
    public const string PointsHeader = "Points";

    public static byte[] Export(string itemTitle, decimal maxPoints, IReadOnlyList<ColumnExportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Grades");
        string[] headers = [StudentNumberHeader, "NameEn", "NameAr", PointsHeader, "MaxPoints", "Item"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Cell(1, c + 1).Style.Font.Bold = true;
        }

        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).SetValue(SpreadsheetCell.Safe(row.StudentNumber));
            sheet.Cell(r, 2).SetValue(SpreadsheetCell.Safe(row.NameEn));
            sheet.Cell(r, 3).SetValue(SpreadsheetCell.Safe(row.NameAr));
            if (row.Points is { } points)
            {
                sheet.Cell(r, 4).SetValue(points);
            }

            sheet.Cell(r, 5).SetValue(maxPoints);
            sheet.Cell(r, 6).SetValue(SpreadsheetCell.Safe(itemTitle));
            r++;
        }

        sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
