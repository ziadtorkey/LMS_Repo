using System.Globalization;
using ClosedXML.Excel;
using Lms.Shared.Attendance;

namespace Lms.Api.Features.Attendance;

/// <summary>
/// The Excel file of an attendance report (FR-ATT-01, M5 Task 20). Every text cell goes through <see cref="Safe"/> (the same rule as the audit export, L9): a name or
/// code that starts like a formula is stored as text, never evaluated. The file says "as of" when the numbers were computed, because an approved excuse changes history.
/// </summary>
public static class AttendanceReportExporter
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Headers =
        ["Student (ar)", "Student (en)", "Student number", "Course (ar)", "Course (en)", "Present", "Absent", "Late", "Excused", "Total", "Attendance %"];

    public static byte[] ToXlsx(AttendanceReportDto report)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Attendance");
        sheet.Cell(1, 1).SetValue(Safe($"Attendance report ({report.Kind}) {report.From:yyyy-MM-dd} to {report.To:yyyy-MM-dd}, as of {report.ComputedAtUtc:yyyy-MM-dd HH:mm} UTC"));
        sheet.Cell(1, 1).Style.Font.Bold = true;
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = sheet.Cell(3, c + 1);
            cell.SetValue(Headers[c]);
            cell.Style.Font.Bold = true;
        }

        var r = 4;
        foreach (var row in report.Rows.Items)
        {
            string[] texts = [row.StudentNameAr, row.StudentNameEn, row.StudentNumber, row.CourseNameAr ?? string.Empty, row.CourseNameEn ?? string.Empty];
            for (var c = 0; c < texts.Length; c++)
            {
                sheet.Cell(r, c + 1).SetValue(Safe(texts[c]));
            }

            var s = row.Summary;
            sheet.Cell(r, 6).SetValue(s.Present);
            sheet.Cell(r, 7).SetValue(s.Absent);
            sheet.Cell(r, 8).SetValue(s.Late);
            sheet.Cell(r, 9).SetValue(s.Excused);
            sheet.Cell(r, 10).SetValue(s.Total);
            sheet.Cell(r, 11).SetValue(s.AttendancePercent?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty);
            r++;
        }

        sheet.SheetView.FreezeRows(3);
        sheet.Columns().AdjustToContents(1, 100);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Prefixes text that starts like a formula (CSV/Excel injection); the same rule as <c>AuditExporter</c> and BE1's <c>SpreadsheetCell.Safe</c>.</summary>
    public static string Safe(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
}
