using Lms.Domain.LiveSessions;
using Lms.Shared.LiveSessions;

namespace Lms.UnitTests.LiveSessions;

/// <summary>Weekly recurrence expansion in the wall-clock time of an IANA zone (M4b Task 4, Decision 6).</summary>
public sealed class RecurrenceExpanderTests
{
    private static RecurrenceRule Rule(string zone, int interval = 1, int? count = 4, DateTime? until = null, params string[] days) =>
        new(interval, days.Length == 0 ? ["Sunday"] : days, until, count, zone);

    [Fact]
    public void Count_gives_that_many_weekly_occurrences_starting_at_the_start()
    {
        var start = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc); // a Sunday

        var result = RecurrenceExpander.Expand(start, Rule("UTC", count: 3));

        Assert.Equal([start, start.AddDays(7), start.AddDays(14)], result);
    }

    [Fact]
    public void Several_days_and_an_interval_are_honoured_in_order()
    {
        var start = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc); // Sunday

        var result = RecurrenceExpander.Expand(start, Rule("UTC", 2, 4, null, "Sunday", "Tuesday"));

        Assert.Equal(
            [start, start.AddDays(2), start.AddDays(14), start.AddDays(16)], result);
    }

    [Fact]
    public void Until_bounds_the_series_inclusively()
    {
        var start = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);

        var result = RecurrenceExpander.Expand(start, Rule("UTC", count: null, until: start.AddDays(14)));

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void More_than_52_occurrences_is_refused()
    {
        var start = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc);

        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", count: 53)));
        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", count: null, until: start.AddDays(7 * 60))));
        Assert.Equal(52, RecurrenceExpander.Expand(start, Rule("UTC", count: 52)).Count);
    }

    [Fact]
    public void Exactly_one_of_until_and_count_a_known_zone_and_the_start_weekday_are_required()
    {
        var start = new DateTime(2026, 11, 15, 9, 0, 0, DateTimeKind.Utc); // Sunday

        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", count: null)));
        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", count: 2, until: start.AddDays(7))));
        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("Mars/Olympus", count: 2)));
        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", 1, 2, null, "Monday")));
        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", interval: 0, count: 2)));
        Assert.Throws<RecurrenceInvalidException>(() => RecurrenceExpander.Expand(start, Rule("UTC", 1, 2, null, "Funday", "Sunday")));
    }

    [Fact]
    public void Wall_clock_time_is_kept_across_the_dst_change()
    {
        // New York: DST ended 2026-11-01. 09:00 local is 13:00 UTC before and 14:00 UTC after.
        var start = new DateTime(2026, 10, 25, 13, 0, 0, DateTimeKind.Utc); // Sunday 09:00 EDT

        var result = RecurrenceExpander.Expand(start, Rule("America/New_York", count: 3));

        Assert.Equal(new DateTime(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc), result[1]);
        Assert.Equal(new DateTime(2026, 11, 8, 14, 0, 0, DateTimeKind.Utc), result[2]);
    }

    [Fact]
    public void A_wall_time_inside_the_spring_forward_gap_moves_forward_by_the_gap()
    {
        // New York: 2027-03-14 02:30 does not exist; it becomes 03:30 EDT (07:30 UTC).
        var start = new DateTime(2027, 3, 7, 7, 30, 0, DateTimeKind.Utc); // Sunday 02:30 EST
        var result = RecurrenceExpander.Expand(start, Rule("America/New_York", count: 2));

        Assert.Equal(new DateTime(2027, 3, 14, 7, 30, 0, DateTimeKind.Utc), result[1]);
    }

    [Fact]
    public void An_ambiguous_wall_time_uses_its_first_instance()
    {
        // New York: 2026-11-01 01:30 happens twice; the first (EDT, 05:30 UTC) is used.
        var start = new DateTime(2026, 10, 25, 5, 30, 0, DateTimeKind.Utc); // Sunday 01:30 EDT
        var result = RecurrenceExpander.Expand(start, Rule("America/New_York", count: 2));

        Assert.Equal(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc), result[1]);
    }

    [Fact]
    public void Riyadh_has_no_dst_so_every_week_is_seven_days_apart()
    {
        var start = new DateTime(2026, 11, 15, 6, 0, 0, DateTimeKind.Utc);

        var result = RecurrenceExpander.Expand(start, Rule("Asia/Riyadh", count: 3));

        Assert.Equal([start, start.AddDays(7), start.AddDays(14)], result);
    }
}
