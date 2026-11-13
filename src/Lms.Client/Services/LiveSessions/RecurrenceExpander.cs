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
        if (r.IntervalWeeks < LiveSessionLimits.MinIntervalWeeks) throw Invalid();
        if (r.Count is null == r.UntilUtc is null) throw Invalid(); // exactly one end
        if (r.Count is { } c && (c < 1 || c > LiveSessionLimits.MaxOccurrences)) throw Invalid();
        if (r.UntilUtc is { } until && until <= startsAtUtc) throw Invalid();

        var days = r.DaysOfWeek.Select(ParseDay).Distinct().Order().ToList();
        if (days.Count == 0) throw Invalid();

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(r.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw Invalid(); }

        var firstLocal = TimeZoneInfo.ConvertTimeFromUtc(startsAtUtc, zone);
        var weekStart = firstLocal.Date.AddDays(-(int)firstLocal.DayOfWeek);
        var result = new List<DateTime>();
        for (var block = 0; result.Count <= LiveSessionLimits.MaxOccurrences; block++)
        {
            foreach (var day in days)
            {
                var date = weekStart.AddDays(block * 7 * r.IntervalWeeks + (int)day);
                var utc = ToUtc(date + firstLocal.TimeOfDay, zone);
                if (utc < startsAtUtc) continue;
                if (r.UntilUtc is { } end && utc > end) return Finish(result);
                result.Add(utc);
                if (r.Count is { } n && result.Count == n) return result;
            }
        }
        return Finish(result);
    }

    private static IReadOnlyList<DateTime> Finish(List<DateTime> result) =>
        result.Count is 0 or > LiveSessionLimits.MaxOccurrences ? throw Invalid() : result;

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
