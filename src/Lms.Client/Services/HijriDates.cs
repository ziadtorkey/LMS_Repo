using System.Globalization;

namespace Lms.Client.Services;

/// <summary>Display of the Hijri (Umm al-Qura) date next to the Gregorian one, when the unit setting Ui.ShowHijriDates is on (X-01).</summary>
public static class HijriDates
{
    public static string Format(DateOnly date)
    {
        var calendar = new UmAlQuraCalendar();
        var at = date.ToDateTime(TimeOnly.MinValue);
        // Dates outside the calendar's range (1900-2077) are shown without a Hijri part.
        if (at < calendar.MinSupportedDateTime || at > calendar.MaxSupportedDateTime) return "";
        return $"{calendar.GetDayOfMonth(at):00}/{calendar.GetMonth(at):00}/{calendar.GetYear(at)}";
    }
}
