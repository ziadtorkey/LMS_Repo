using Lms.Domain.Comms;

namespace Lms.UnitTests.Comms;

/// <summary>M6 Task 5: the pure rules of the outbox (back-off, quiet hours, the client's day).</summary>
public sealed class DeliveryPolicyTests
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(8, 3600)] // 30 * 2^7 = 3840 s, capped at one hour
    [InlineData(20, 3600)]
    public void Backoff_doubles_from_thirty_seconds_and_caps_at_one_hour(int attempt, int seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), DeliveryPolicy.Backoff(attempt, jitter: 0, retryAfter: null));

    [Fact]
    public void Jitter_moves_the_delay_by_at_most_twenty_percent()
    {
        Assert.Equal(TimeSpan.FromSeconds(48), DeliveryPolicy.Backoff(2, -1, null));
        Assert.Equal(TimeSpan.FromSeconds(72), DeliveryPolicy.Backoff(2, 1, null));
    }

    [Fact]
    public void A_longer_retry_after_wins_and_a_shorter_one_does_not()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), DeliveryPolicy.Backoff(1, 0, TimeSpan.FromMinutes(10)));
        Assert.Equal(TimeSpan.FromSeconds(30), DeliveryPolicy.Backoff(1, 0, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Limits_per_channel_match_the_proposal()
    {
        Assert.Equal(8, DeliveryPolicy.MaxAttempts("Email"));
        Assert.Equal(TimeSpan.FromHours(24), DeliveryPolicy.TimeToLive("Email"));
        Assert.Equal(5, DeliveryPolicy.MaxAttempts("Push"));
    }

    [Fact]
    public void Quiet_hours_wrap_past_midnight()
    {
        var now = DateTime.Parse("2026-11-14T19:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal); // 22:00 local

        var inside = DeliveryPolicy.IsQuiet(now, Riyadh, new TimeOnly(21, 0), new TimeOnly(7, 0), out var endsAt);

        Assert.True(inside);
        Assert.Equal(DateTime.Parse("2026-11-15T04:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal), endsAt); // 07:00 local
    }

    [Fact]
    public void Outside_quiet_hours_there_is_no_hold()
    {
        var noon = DateTime.Parse("2026-11-14T09:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal); // 12:00 local

        Assert.False(DeliveryPolicy.IsQuiet(noon, Riyadh, new TimeOnly(21, 0), new TimeOnly(7, 0), out _));
    }

    [Fact]
    public void A_same_day_window_works_too()
    {
        var now = DateTime.Parse("2026-11-14T10:30:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal); // 13:30 local

        Assert.True(DeliveryPolicy.IsQuiet(now, Riyadh, new TimeOnly(13, 0), new TimeOnly(14, 0), out var endsAt));
        Assert.Equal(DateTime.Parse("2026-11-14T11:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal), endsAt);
    }

    [Fact]
    public void The_client_day_runs_from_local_midnight()
    {
        var now = DateTime.Parse("2026-11-14T22:30:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal); // 01:30 on the 15th local

        var (start, next) = DeliveryPolicy.ClientDay(now, Riyadh);

        Assert.Equal(DateTime.Parse("2026-11-14T21:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal), start);
        Assert.Equal(DateTime.Parse("2026-11-15T21:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal), next);
    }
}
