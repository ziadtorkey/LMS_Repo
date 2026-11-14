namespace Lms.Domain.Calendar;

/// <summary>
/// The one place that turns instants into the client's local school days (M5 L2, D64). Generation, edit windows, excuse deadlines, alert windows and
/// publish-at displays all go through it; no other M5 code converts to local days. The client time zone is the client-level setting
/// <c>Org.TimeZone</c> (default <c>Asia/Riyadh</c>, also when the setting is missing or not a known zone). The unit-based methods throw
/// <see cref="InvalidOperationException"/> for a unit outside the caller's scope: callers check unit access first (a miss is 404 at the API).
/// </summary>
public interface IClientCalendar
{
    /// <summary>The client's time zone.</summary>
    Task<TimeZoneInfo> GetTimeZoneAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The client's local date now.</summary>
    Task<DateOnly> LocalTodayAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The client's local date of a UTC instant.</summary>
    Task<DateOnly> LocalDateAsync(Guid organizationId, DateTime instantUtc, CancellationToken cancellationToken);

    /// <summary>True when the weekday is in the unit's effective <c>Timetable.WorkingDays</c> (Sun-Thu by default).</summary>
    Task<bool> IsWorkingDayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>True when a Holiday of the unit's effective academic years (at the unit or an ancestor) covers the date.</summary>
    Task<bool> IsHolidayAsync(Guid orgUnitId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// The number of school days (working weekday, not a holiday) strictly after <paramref name="after"/> up to and including <paramref name="through"/>;
    /// 0 on the same day or when <paramref name="through"/> is earlier. Both are client-local dates (get today from <see cref="LocalTodayAsync"/>).
    /// </summary>
    Task<int> SchoolDaysBetweenAsync(Guid orgUnitId, DateOnly after, DateOnly through, CancellationToken cancellationToken);

    /// <summary>
    /// The date <paramref name="schoolDays"/> working, non-holiday days after (positive) or before (negative) <paramref name="from"/>; 0 returns
    /// <paramref name="from"/>. Throws <see cref="InvalidOperationException"/> when the unit has no working day to count on.
    /// </summary>
    Task<DateOnly> AddSchoolDaysAsync(Guid orgUnitId, DateOnly from, int schoolDays, CancellationToken cancellationToken);
}
