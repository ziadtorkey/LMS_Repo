namespace Lms.Client.Pages.Audit;

/// <summary>Converts the picked calendar days (the user's local days, as the table shows local times) to UTC bounds.</summary>
public static class AuditDateRange
{
    /// <summary>Start of <paramref name="day"/> in <paramref name="zone"/>, as UTC.</summary>
    public static DateTime StartUtc(DateTime day, TimeZoneInfo zone) => ToUtc(day.Date, zone);

    /// <summary>Last tick of <paramref name="day"/> in <paramref name="zone"/>, as UTC (the end bound is inclusive).</summary>
    public static DateTime EndUtc(DateTime day, TimeZoneInfo zone) => ToUtc(day.Date.AddDays(1), zone).AddTicks(-1);

    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1); // a DST gap at midnight
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }
}
