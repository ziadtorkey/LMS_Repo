using Lms.Infrastructure.Attendance;
using Lms.Shared.Attendance;

namespace Lms.UnitTests.Attendance;

public sealed class AttendanceSummariesTests
{
    [Fact]
    public void Statuses_are_counted_and_unmarked_rows_are_not()
    {
        var summary = AttendanceSummaries.Summarize(["Present", "Present", "Absent", "Late", "Excused", null]);

        Assert.Equal(new AttendanceSummaryDto(2, 1, 1, 1, 5, 80m), summary);
    }

    [Fact]
    public void Nothing_taken_has_no_percentage()
    {
        Assert.Null(AttendanceSummaries.Summarize([]).AttendancePercent);
        Assert.Equal(0, AttendanceSummaries.Summarize([null]).Total);
    }

    [Fact]
    public void The_percentage_has_two_decimals()
    {
        Assert.Equal(66.67m, AttendanceSummaries.Summarize(["Present", "Late", "Absent"]).AttendancePercent);
    }

    [Theory]
    [InlineData("Absent", true)]
    [InlineData("Late", true)]
    [InlineData("Present", false)]
    [InlineData("Excused", false)]
    [InlineData(null, false)]
    public void Only_absent_and_late_are_noticeable(string? status, bool noticeable) =>
        Assert.Equal(noticeable, AbsenceNotifier.IsNoticeable(status));
}
