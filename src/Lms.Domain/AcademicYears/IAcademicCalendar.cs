namespace Lms.Domain.AcademicYears;

public interface IAcademicCalendar
{
    /// <summary>True when a Holiday covers <paramref name="date"/> at the unit or any of its ancestors (used by timetable/attendance).</summary>
    Task<bool> IsNonWorkingDayAsync(string orgUnitPath, DateOnly date, CancellationToken cancellationToken);
}
