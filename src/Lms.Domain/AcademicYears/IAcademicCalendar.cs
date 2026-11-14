namespace Lms.Domain.AcademicYears;

public interface IAcademicCalendar
{
    /// <summary>True when a Holiday covers <paramref name="date"/> at the unit or any of its ancestors (used by timetable/attendance).</summary>
    Task<bool> IsNonWorkingDayAsync(string orgUnitPath, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Every date of <paramref name="from"/>..<paramref name="to"/> covered by a Holiday at the unit or an ancestor: one query for the whole range.</summary>
    Task<IReadOnlySet<DateOnly>> HolidayDatesAsync(string orgUnitPath, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
