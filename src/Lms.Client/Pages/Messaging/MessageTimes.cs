using System.Globalization;
using Microsoft.Extensions.Localization;
using Lms.Client.Resources;

namespace Lms.Client.Pages.Messaging;

/// <summary>The time labels of the inbox and the thread (spec #615 §1): the time today, "Yesterday", or yyyy-MM-dd, all in the browser's time zone (TimeProvider.LocalTimeZone) until the client calendar of D64 is on main.</summary>
public static class MessageTimes
{
    public static string Day(DateTime utc, DateTime nowUtc, TimeZoneInfo zone, IStringLocalizer<SharedResource> l)
    {
        var d = Local(utc, zone).Date;
        var today = Local(nowUtc, zone).Date;
        if (d == today) return l["Msg_Day_Today"];
        if (d == today.AddDays(-1)) return l["Msg_Day_Yesterday"];
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>For a list row: the time when it is today, otherwise the day label.</summary>
    public static string Row(DateTime utc, DateTime nowUtc, TimeZoneInfo zone, IStringLocalizer<SharedResource> l) =>
        Local(utc, zone).Date == Local(nowUtc, zone).Date ? Clock(utc, zone) : Day(utc, nowUtc, zone, l);

    public static string Clock(DateTime utc, TimeZoneInfo zone) => Local(utc, zone).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The day a message belongs to, in <paramref name="zone"/>.</summary>
    public static DateTime DayOf(DateTime utc, TimeZoneInfo zone) => Local(utc, zone).Date;

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
}
