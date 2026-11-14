using Lms.Shared.Attendance;

namespace Lms.Infrastructure.Attendance;

/// <summary>Counts of statuses over taken sessions (FR-ATT-01): the one place that turns marks into a summary, so the reports and the own reads agree.</summary>
public static class AttendanceSummaries
{
    /// <summary>Counts per status; <c>AttendancePercent</c> = (Present + Late + Excused) / Total, two decimals, null when nothing was taken. Unmarked (null) rows are not counted.</summary>
    public static AttendanceSummaryDto Summarize(IEnumerable<string?> statuses)
    {
        int present = 0, absent = 0, late = 0, excused = 0;
        foreach (var status in statuses)
        {
            switch (status)
            {
                case AttendanceStatuses.Present: present++; break;
                case AttendanceStatuses.Absent: absent++; break;
                case AttendanceStatuses.Late: late++; break;
                case AttendanceStatuses.Excused: excused++; break;
            }
        }

        var total = present + absent + late + excused;
        decimal? percent = total == 0 ? null : Math.Round((present + late + excused) * 100m / total, 2, MidpointRounding.AwayFromZero);
        return new AttendanceSummaryDto(present, absent, late, excused, total, percent);
    }
}
