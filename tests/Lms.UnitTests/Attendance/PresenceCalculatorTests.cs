using Lms.Infrastructure.Attendance.Live;

namespace Lms.UnitTests.Attendance;

/// <summary>Presence from live stays (M5 Task 18, L11 item 6).</summary>
public sealed class PresenceCalculatorTests
{
    private static readonly DateTime Start = new(2027, 2, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(60);

    private static PresenceResult Run(params PresenceSegment[] stays) => PresenceCalculator.Evaluate(Start, End, stays, 50, 10);

    [Fact]
    public void Presence_merges_overlapping_segments()
    {
        // Two overlapping stays (a rejoin and a second device) must not count the overlap twice: 0-20 and 10-40 are 40 minutes, not 50.
        var result = Run(new PresenceSegment(Start, Start.AddMinutes(20)), new PresenceSegment(Start.AddMinutes(10), Start.AddMinutes(40)));

        Assert.Equal(40, result.Minutes);
        Assert.Equal("Present", result.Status);
    }

    [Fact]
    public void Gaps_are_not_counted_and_a_short_total_is_absent()
    {
        var result = Run(new PresenceSegment(Start, Start.AddMinutes(10)), new PresenceSegment(Start.AddMinutes(30), Start.AddMinutes(40)));

        Assert.Equal((20, "Absent"), (result.Minutes, result.Status)); // 20 of 60 minutes is under 50 percent
    }

    [Fact]
    public void Late_uses_the_first_join()
    {
        var late = Run(new PresenceSegment(Start.AddMinutes(11), End));
        var onTime = Run(new PresenceSegment(Start.AddMinutes(10), End));
        var rejoined = Run(new PresenceSegment(Start.AddMinutes(2), Start.AddMinutes(5)), new PresenceSegment(Start.AddMinutes(20), End));

        Assert.Equal(["Late", "Present", "Present"], [late.Status, onTime.Status, rejoined.Status]); // the first join counts, not the last
        Assert.Equal(Start.AddMinutes(2), rejoined.FirstJoinedAtUtc);
    }

    [Fact]
    public void Stays_are_clipped_to_the_scheduled_window_and_an_open_stay_runs_to_the_end()
    {
        var result = Run(new PresenceSegment(Start.AddMinutes(-30), null)); // joined early, never left

        Assert.Equal((60, "Present", Start), (result.Minutes, result.Status, result.FirstJoinedAtUtc));
        Assert.Equal("Absent", Run(new PresenceSegment(End.AddMinutes(5), End.AddMinutes(30))).Status); // after the end: not present
        Assert.Equal("Absent", Run().Status);
    }
}
