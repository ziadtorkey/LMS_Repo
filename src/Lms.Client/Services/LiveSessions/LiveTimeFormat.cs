using System.Globalization;
using Lms.Client.Resources;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Services.LiveSessions;

/// <summary>
/// Countdown and elapsed-time text for live sessions: the largest two units ("2 h 10 min", "بعد ساعتين و10 دقائق"), with the
/// shared Plurals helper (a counted noun is never shown as "2 ساعة"). Digits are always Western.
/// </summary>
public static class LiveTimeFormat
{
    /// <summary>"2 days 3 h", "2 h 10 min", "8 min" or "less than a minute" (the unit words come from the resources).</summary>
    public static string Describe(TimeSpan span, IStringLocalizer<SharedResource> l, bool arabic)
    {
        if (span < TimeSpan.FromMinutes(1)) return l["Live_LessThanMinute"].Value;
        var parts = new List<string>();
        var days = (int)span.TotalDays;
        if (days > 0)
        {
            parts.Add(Unit("Day", days, l, arabic));
            if (span.Hours > 0) parts.Add(Unit("Hour", span.Hours, l, arabic));
        }
        else if ((int)span.TotalHours > 0)
        {
            parts.Add(Unit("Hour", (int)span.TotalHours, l, arabic));
            if (span.Minutes > 0) parts.Add(Unit("Minute", span.Minutes, l, arabic));
        }
        else
        {
            parts.Add(Unit("Minute", (int)span.TotalMinutes, l, arabic));
        }
        return parts.Count == 1 ? parts[0] : string.Format(CultureInfo.InvariantCulture, l["Live_Join_Pair"].Value, parts[0], parts[1]);
    }

    private static string Unit(string unit, int n, IStringLocalizer<SharedResource> l, bool arabic) => l.Count($"Live_Unit_{unit}", n, arabic);
}
