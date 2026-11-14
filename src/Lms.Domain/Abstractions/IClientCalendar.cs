namespace Lms.Domain.Abstractions;

/// <summary>
/// The client calendar of M5 (plan L2): one place that answers school-day questions in the client time zone (D64). Task 13 implements it
/// and extends it with the local date of an instant, working-day and holiday checks; Task 19 (excuses) needs only the school-day count.
/// </summary>
public interface IClientCalendar
{
    /// <summary>
    /// The number of school days strictly after <paramref name="absenceDate"/> up to and including <paramref name="today"/> (0 on the same day):
    /// <c>Timetable.WorkingDays</c> of the unit minus the holidays of the unit's year.
    /// </summary>
    int SchoolDaysSince(DateOnly absenceDate, DateOnly today);
}
