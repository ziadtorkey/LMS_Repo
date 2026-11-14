namespace Lms.Client.Components.Gradebook;

/// <summary>A wall-clock time picked in the client zone, as the UTC instant the server wants (D64).</summary>
public static class ScheduleTime
{
    /// <summary>False when the time does not exist in the zone (the hour skipped when the clocks go forward); an ambiguous time takes the standard offset.</summary>
    public static bool TryToUtc(DateTime date, TimeSpan time, TimeZoneInfo zone, out DateTime utc)
    {
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            utc = default;
            return false;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return true;
    }
}
