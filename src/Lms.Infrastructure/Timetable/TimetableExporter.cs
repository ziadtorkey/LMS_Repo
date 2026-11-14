using System.Globalization;
using ClosedXML.Excel;
using Lms.Infrastructure.Spreadsheets;
using Lms.Shared.Timetable;

namespace Lms.Infrastructure.Timetable;

/// <summary>
/// The timetable of one class and term as an Excel grid (FR-TT-04): one row per period, one column per working day. Every cell that carries user-typed
/// text (course, teacher, room, period and class names) goes through <see cref="SpreadsheetCell.Safe"/> and is stored as text, so no value is a formula.
/// </summary>
public static class TimetableExporter
{
    public sealed record PeriodRow(string NameAr, string NameEn, TimeOnly Start, TimeOnly End, Guid Id);

    public static void Write(string className, IReadOnlyList<PeriodRow> periods, IReadOnlyList<DayOfWeek> days, IReadOnlyList<SlotDto> slots, Stream output)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Timetable");
        Put(sheet, 1, 1, className);
        sheet.Cell(1, 1).Style.Font.Bold = true;
        for (var d = 0; d < days.Count; d++)
        {
            Put(sheet, 2, d + 2, $"{CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(days[d])} / {new CultureInfo("ar").DateTimeFormat.GetDayName(days[d])}");
            sheet.Cell(2, d + 2).Style.Font.Bold = true;
        }

        for (var p = 0; p < periods.Count; p++)
        {
            var period = periods[p];
            Put(sheet, p + 3, 1, $"{period.NameEn} / {period.NameAr} ({period.Start:HH:mm}-{period.End:HH:mm})");
            for (var d = 0; d < days.Count; d++)
            {
                var cell = slots.Where(s => s.PeriodId == period.Id && s.DayOfWeek == days[d]).Select(Describe);
                Put(sheet, p + 3, d + 2, string.Join("\n", cell));
            }
        }

        sheet.SheetView.FreezeRows(2);
        sheet.Columns().AdjustToContents(1, 60);
        workbook.SaveAs(output);
    }

    private static string Describe(SlotDto s)
    {
        var course = string.IsNullOrEmpty(s.CourseNameEn) ? s.CourseNameAr : s.CourseNameEn;
        var teacher = string.IsNullOrEmpty(s.TeacherName) ? string.Empty : $" - {s.TeacherName}";
        var room = string.IsNullOrEmpty(s.RoomName) ? string.Empty : $" [{s.RoomName}]";
        return course + teacher + room;
    }

    private static void Put(IXLWorksheet sheet, int row, int column, string text)
    {
        var cell = sheet.Cell(row, column);
        cell.SetValue(SpreadsheetCell.Safe(text));
    }
}
