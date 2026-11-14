using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomWebhookRateLimitTests
{
    private static (ZoomWebhookRateLimiter Limiter, ManualClock Clock) Build(int failuresPerMinute = 5, int globalPerMinute = 1000)
    {
        var clock = new ManualClock(new DateTime(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        return (new ZoomWebhookRateLimiter(clock, failuresPerMinute, globalPerMinute), clock);
    }

    [Fact]
    public void An_address_is_blocked_only_after_the_threshold_of_failed_verifications()
    {
        var (limiter, _) = Build(failuresPerMinute: 3);

        for (var i = 0; i < 3; i++)
        {
            Assert.False(limiter.IsBlocked("203.0.113.7"));
            limiter.RegisterFailure("203.0.113.7");
        }

        Assert.True(limiter.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void Failures_of_one_address_do_not_block_another()
    {
        var (limiter, _) = Build(failuresPerMinute: 1);

        limiter.RegisterFailure("203.0.113.7");

        Assert.True(limiter.IsBlocked("203.0.113.7"));
        Assert.False(limiter.IsBlocked("203.0.113.8"));
    }

    [Fact]
    public void The_block_lifts_when_the_minute_has_passed()
    {
        var (limiter, clock) = Build(failuresPerMinute: 1);
        limiter.RegisterFailure("203.0.113.7");

        clock.Advance(TimeSpan.FromSeconds(61));

        Assert.False(limiter.IsBlocked("203.0.113.7"));
    }

    [Fact]
    public void Valid_traffic_is_never_counted_as_a_failure_so_a_valid_burst_is_not_limited_per_address()
    {
        var (limiter, _) = Build(failuresPerMinute: 1);

        for (var i = 0; i < 500; i++)
        {
            Assert.True(limiter.TryAdmit());
            Assert.False(limiter.IsBlocked("198.51.100.1"));
        }
    }

    [Fact]
    public void The_global_ceiling_refuses_only_beyond_a_very_high_rate_and_resets_each_minute()
    {
        var (limiter, clock) = Build(globalPerMinute: 10);

        for (var i = 0; i < 10; i++)
        {
            Assert.True(limiter.TryAdmit());
        }

        Assert.False(limiter.TryAdmit());
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(limiter.TryAdmit());
    }

    [Fact]
    public void The_failure_table_stays_bounded_under_a_spray_of_addresses()
    {
        var (limiter, _) = Build();

        for (var i = 0; i < 50_000; i++)
        {
            limiter.RegisterFailure("10.0." + (i / 256 % 256) + "." + (i % 256) + "-" + i);
        }

        Assert.True(limiter.TrackedAddresses <= ZoomWebhookRateLimiter.MaxTrackedAddresses);
    }
}
