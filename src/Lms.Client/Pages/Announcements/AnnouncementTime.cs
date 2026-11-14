namespace Lms.Client.Pages.Announcements;

/// <summary>A wall-clock time picked in the client zone, as the UTC instant the server wants (D64).</summary>
public static class AnnouncementTime
{
    /// <summary>False when the time does not exist in the zone (the hour the clocks skip); an ambiguous time takes the standard offset.</summary>
    public static bool TryToUtc(DateTime local, TimeZoneInfo zone, out DateTime utc)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            utc = default;
            return false;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        return true;
    }
}
