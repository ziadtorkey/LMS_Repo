namespace Lms.Domain.LiveSessions;

/// <summary>A weekly recurrence as the domain sees it (the request's <c>LiveRecurrenceInput</c> without the transport type).</summary>
public sealed record RecurrenceRule(int IntervalWeeks, IReadOnlyList<string> DaysOfWeek, DateTime? UntilUtc, int? Count, string TimeZoneId);

/// <summary>The recurrence is not valid (maps to 400 <c>recurrence_invalid</c>).</summary>
public sealed class RecurrenceInvalidException(string message) : Exception(message);

/// <summary>
/// Expands a weekly recurrence into UTC start times (M4b Decision 6). Every occurrence is computed in the wall-clock time of
/// the IANA zone, so it keeps its local time across DST changes; a local time inside a spring-forward gap moves forward by the
/// gap, an ambiguous one (fall-back overlap) uses its first instance. The first occurrence is the start itself.
/// </summary>
public static class RecurrenceExpander
{
    public static IReadOnlyList<DateTime> Expand(DateTime startUtc, RecurrenceRule rule)
    {
        if (rule.IntervalWeeks < LiveSessionRules.MinIntervalWeeks)
        {
            throw new RecurrenceInvalidException("The week interval must be at least 1.");
        }

        if ((rule.UntilUtc is null) == (rule.Count is null))
        {
            throw new RecurrenceInvalidException("Give exactly one of an end date and an occurrence count.");
        }

        if (rule.Count is { } count && (count < 1 || count > LiveSessionRules.MaxOccurrences))
        {
            throw new RecurrenceInvalidException($"A series has 1 to {LiveSessionRules.MaxOccurrences} occurrences.");
        }

        var days = ParseDays(rule.DaysOfWeek);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(rule.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new RecurrenceInvalidException("Unknown time zone.");
        }

        var first = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);
        if (!days.Contains(first.DayOfWeek))
        {
            throw new RecurrenceInvalidException("The start must fall on one of the chosen weekdays.");
        }

        var weekStart = first.Date.AddDays(-(int)first.DayOfWeek); // the Sunday of the first week
        var result = new List<DateTime>();
        for (var week = 0; ; week++)
        {
            foreach (var day in days.Order())
            {
                var date = weekStart.AddDays(week * 7 * rule.IntervalWeeks + (int)day);
                if (date < first.Date)
                {
                    continue;
                }

                var utc = result.Count == 0 ? DateTime.SpecifyKind(startUtc, DateTimeKind.Utc) : ToUtc(date + first.TimeOfDay, zone);
                if (rule.UntilUtc is { } until && utc > DateTime.SpecifyKind(until, DateTimeKind.Utc))
                {
                    return result;
                }

                result.Add(utc);
                if (rule.Count is { } c && result.Count >= c)
                {
                    return result;
                }

                if (result.Count > LiveSessionRules.MaxOccurrences)
                {
                    throw new RecurrenceInvalidException($"A series has at most {LiveSessionRules.MaxOccurrences} occurrences.");
                }
            }
        }
    }

    private static HashSet<DayOfWeek> ParseDays(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            throw new RecurrenceInvalidException("Choose at least one weekday.");
        }

        var days = new HashSet<DayOfWeek>();
        foreach (var name in names)
        {
            if (!Enum.TryParse<DayOfWeek>(name, out var day) || !Enum.GetNames<DayOfWeek>().Contains(name))
            {
                throw new RecurrenceInvalidException("Unknown weekday.");
            }

            days.Add(day);
        }

        return days;
    }

    /// <summary>One wall-clock time of the zone as a UTC instant (gap: forward by the gap; overlap: the first instance).</summary>
    internal static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local))
        {
            offset = zone.GetUtcOffset(local.AddHours(-3)); // the offset in force before the gap
        }
        else if (zone.IsAmbiguousTime(local))
        {
            offset = zone.GetAmbiguousTimeOffsets(local).Max(); // the daylight instance comes first
        }
        else
        {
            offset = zone.GetUtcOffset(local);
        }

        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    /// <summary>The wall-clock time of a UTC instant in the zone (for shifting a series by a local delta).</summary>
    internal static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>Moves an occurrence by the wall-clock delta between an old and a new start in the series zone.</summary>
    public static DateTime Shift(DateTime occurrenceUtc, DateTime oldStartUtc, DateTime newStartUtc, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var delta = ToLocal(newStartUtc, zone) - ToLocal(oldStartUtc, zone);
        return ToUtc(ToLocal(occurrenceUtc, zone) + delta, zone);
    }
}
