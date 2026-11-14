using ClosedXML.Excel;
using Lms.Api.Features.Attendance;
using Lms.Shared;
using Lms.Shared.Attendance;

namespace Lms.IntegrationTests.Attendance;

/// <summary>The attendance report Excel file (M5 Task 20): the numbers of the report, text cells that cannot become formulas, and the "as of" time.</summary>
public sealed class AttendanceReportExportTests
{
    private static AttendanceReportDto Report(params AttendanceReportRowDto[] rows) =>
        new(AttendanceReportKinds.Class, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), new AttendanceSummaryDto(1, 1, 0, 0, 2, 50m),
            new PagedResult<AttendanceReportRowDto>(rows, rows.Length, 1, Math.Max(1, rows.Length)), new DateTime(2027, 2, 1, 8, 30, 0, DateTimeKind.Utc), DateTime.UtcNow);

    private static IXLWorksheet Open(byte[] bytes) => new XLWorkbook(new MemoryStream(bytes)).Worksheet("Attendance");

    [Fact]
    public void The_file_carries_the_counts_the_percentage_and_the_as_of_time()
    {
        var bytes = AttendanceReportExporter.ToXlsx(Report(
            new AttendanceReportRowDto(Guid.NewGuid(), "سارة", "Sara", "000123", null, null, null, new AttendanceSummaryDto(8, 1, 1, 0, 10, 90m))));

        var sheet = Open(bytes);

        Assert.Contains("as of 2027-02-01 08:30 UTC", sheet.Cell(1, 1).GetString());
        Assert.Equal("Student number", sheet.Cell(3, 3).GetString());
        Assert.Equal(("Sara", "000123"), (sheet.Cell(4, 2).GetString(), sheet.Cell(4, 3).GetString()));
        Assert.Equal((8, 1, 1, 0, 10, "90.00"), ((int)sheet.Cell(4, 6).Value, (int)sheet.Cell(4, 7).Value, (int)sheet.Cell(4, 8).Value, (int)sheet.Cell(4, 9).Value,
            (int)sheet.Cell(4, 10).Value, sheet.Cell(4, 11).GetString()));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\",\"x\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void A_name_that_looks_like_a_formula_is_stored_as_text(string name)
    {
        var bytes = AttendanceReportExporter.ToXlsx(Report(
            new AttendanceReportRowDto(Guid.NewGuid(), name, name, name, null, name, name, new AttendanceSummaryDto(0, 0, 0, 0, 0, null))));

        var sheet = Open(bytes);

        Assert.All(new[] { 1, 2, 3, 4, 5 }, c =>
        {
            // The leading apostrophe is Excel's text marker (ClosedXML keeps it as the quote-prefix style): the cell is text, never a formula.
            Assert.Equal(name, sheet.Cell(4, c).GetString());
            Assert.Equal(XLDataType.Text, sheet.Cell(4, c).DataType);
            Assert.False(sheet.Cell(4, c).HasFormula);
        });
        Assert.Equal(string.Empty, sheet.Cell(4, 11).GetString()); // nothing taken: no percentage
    }
}
