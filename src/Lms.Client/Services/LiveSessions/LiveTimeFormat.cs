using System.Globalization;
using Lms.Client.Resources;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Services.LiveSessions;

/// <summary>The plural form of a counted unit. English uses One and Many; Arabic has One, Two, Few (3 to 10) and Many (11 and up).</summary>
public enum PluralForm { One, Two, Few, Many }

/// <summary>
/// Countdown and elapsed-time text for live sessions: the largest two units ("2 h 10 min", "بعد ساعتين و10 دقائق"), with the
/// Arabic plural forms of the M4b design spec (a counted noun is never shown as "2 ساعة"). Digits are always Western.
/// </summary>
public static class LiveTimeFormat
{
    public static PluralForm Form(int n, bool arabic)
    {
        if (n == 1) return PluralForm.One;
        if (!arabic) return PluralForm.Many;
        if (n == 2) return PluralForm.Two;
        var tail = n % 100;
        return tail is >= 3 and <= 10 ? PluralForm.Few : PluralForm.Many;
    }

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

    private static string Unit(string unit, int n, IStringLocalizer<SharedResource> l, bool arabic) =>
        string.Format(CultureInfo.InvariantCulture, l[$"Live_Unit_{unit}_{Form(n, arabic)}"].Value, n);
}
