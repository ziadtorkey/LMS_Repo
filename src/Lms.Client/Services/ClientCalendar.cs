namespace Lms.Client.Services;

/// <summary>
/// The client's local calendar day (D64: the time zone setting <c>Org.TimeZone</c>, Asia/Riyadh by default). Session dates are local days,
/// so they are never compared with the UTC date of an instant: between 00:00 and 03:00 in Riyadh the UTC date is still yesterday.
/// </summary>
public static class ClientCalendar
{
    public const string DefaultZoneId = "Asia/Riyadh";

    private static readonly TimeZoneInfo FixedRiyadh = TimeZoneInfo.CreateCustomTimeZone(DefaultZoneId, TimeSpan.FromHours(3), "Riyadh", "Riyadh"); // no DST

    /// <summary>The zone by id; where the browser has no tz data, Asia/Riyadh falls back to its fixed +03:00 offset.</summary>
    public static TimeZoneInfo Zone(string? id = null)
    {
        id ??= DefaultZoneId;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return id == DefaultZoneId ? FixedRiyadh : Zone(DefaultZoneId); // an unknown id is the default, never UTC
        }
    }

    /// <summary>The local day of an instant.</summary>
    public static DateOnly Today(DateTime utc, TimeZoneInfo? zone = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone ?? Zone()));
}
