using System.Globalization;
using Microsoft.Extensions.Localization;
using Lms.Client.Resources;

namespace Lms.Client.Pages.Messaging;

/// <summary>The time labels of the inbox and the thread (spec #615 §1): the time today, "Yesterday", or yyyy-MM-dd, all in the viewer's local time.</summary>
public static class MessageTimes
{
    public static string Day(DateTime utc, DateTime nowUtc, IStringLocalizer<SharedResource> l)
    {
        var d = Local(utc).Date;
        var today = Local(nowUtc).Date;
        if (d == today) return l["Msg_Day_Today"];
        if (d == today.AddDays(-1)) return l["Msg_Day_Yesterday"];
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>For a list row: the time when it is today, otherwise the day label.</summary>
    public static string Row(DateTime utc, DateTime nowUtc, IStringLocalizer<SharedResource> l) =>
        Local(utc).Date == Local(nowUtc).Date ? Clock(utc) : Day(utc, nowUtc, l);

    public static string Clock(DateTime utc) => Local(utc).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}
