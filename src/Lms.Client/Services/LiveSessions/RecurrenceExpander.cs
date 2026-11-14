using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Services.LiveSessions;

/// <summary>
/// Expands a weekly recurrence into UTC start times the way the server does (Decision 6): the wall-clock time of the first
/// start in the recurrence's IANA zone, on the chosen weekdays, every N weeks (weeks run Sunday to Saturday and start with
/// the week of the first start), DST-aware. Throws 400 <c>recurrence_invalid</c> for every rule the contract lists.
/// </summary>
internal static class RecurrenceExpander
{
    public static IReadOnlyList<DateTime> Expand(DateTime startsAtUtc, LiveRecurrenceInput r)
    {
        var all = ExpandUpTo(startsAtUtc, r, LiveSessionLimits.MaxOccurrences);
        return all.Count is 0 or > LiveSessionLimits.MaxOccurrences ? throw Invalid() : all;
    }

    /// <summary>
    /// For the schedule dialog's preview: the same rules, but a series longer than the limit is reported (as
    /// <c>count</c> = limit + 1) instead of refused, so the dialog can say so.
    /// </summary>
    public static (int Count, IReadOnlyList<DateTime> Dates)? Preview(DateTime startsAtUtc, LiveRecurrenceInput r)
    {
        try
        {
            var all = ExpandUpTo(startsAtUtc, r, LiveSessionLimits.MaxOccurrences + 1);
            return all.Count == 0 ? null : (all.Count, all);
        }
        catch (ApiProblemException)
        {
            return null;
        }
    }

    private static IReadOnlyList<DateTime> ExpandUpTo(DateTime startsAtUtc, LiveRecurrenceInput r, int cap)
    {
        if (r.IntervalWeeks < LiveSessionLimits.MinIntervalWeeks) throw Invalid();
        if (r.Count is null == r.UntilUtc is null) throw Invalid(); // exactly one end
        if (r.Count is { } c && (c < 1 || (c > LiveSessionLimits.MaxOccurrences && cap == LiveSessionLimits.MaxOccurrences))) throw Invalid();
        if (r.UntilUtc is { } until && until <= startsAtUtc) throw Invalid();

        var days = r.DaysOfWeek.Select(ParseDay).Distinct().Order().ToList();
        if (days.Count == 0) throw Invalid();

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(r.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw Invalid(); }

        var firstLocal = TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, zone);
        var weekStart = firstLocal.Date.AddDays(-(int)firstLocal.DayOfWeek);
        var result = new List<DateTime>();
        for (var block = 0; result.Count <= cap; block++)
        {
            foreach (var day in days)
            {
                var date = weekStart.AddDays(block * 7 * r.IntervalWeeks + (int)day);
                var utc = ToUtc(date + firstLocal.TimeOfDay, zone);
                if (utc < startsAtUtc) continue;
                if (r.UntilUtc is { } end && utc > end) return result;
                result.Add(utc);
                if (r.Count is { } n && result.Count == n) return result;
                if (result.Count > cap) return result;
            }
        }
        return result;
    }

    private static DateTime ToUtc(DateTime wallClock, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1); // skipped by the spring-forward gap
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), DateTimeKind.Utc);
    }

    private static DayOfWeek ParseDay(string name) =>
        Enum.TryParse<DayOfWeek>(name, out var d) && Enum.IsDefined(d) && RecurrenceDays.All.Contains(name) ? d : throw Invalid();

    private static ApiProblemException Invalid() => new(400, ProblemCodes.recurrence_invalid, null, null);
}
