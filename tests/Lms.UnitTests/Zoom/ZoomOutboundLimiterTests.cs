using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomOutboundLimiterTests
{
    private static (ZoomOutboundLimiter Limiter, ManualClock Clock, List<TimeSpan> Waits) Build(int maxQueue = 100)
    {
        var clock = new ManualClock(new DateTime(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc));
        var waits = new List<TimeSpan>();
        var limiter = new ZoomOutboundLimiter(clock, (d, _) =>
        {
            waits.Add(d);
            clock.Advance(d);
            return Task.CompletedTask;
        }, maxQueue);
        return (limiter, clock, waits);
    }

    [Fact]
    public async Task A_burst_up_to_the_class_rate_is_not_delayed()
    {
        var (limiter, _, waits) = Build();

        for (var i = 0; i < ZoomOutboundLimiter.PerSecond(ZoomRateClass.Heavy); i++)
        {
            await limiter.AcquireAsync(ZoomRateClass.Heavy);
        }

        Assert.Empty(waits);
    }

    [Fact]
    public async Task Beyond_the_burst_calls_wait_for_the_bucket_to_refill()
    {
        var (limiter, _, waits) = Build();
        var rate = ZoomOutboundLimiter.PerSecond(ZoomRateClass.Heavy);
        for (var i = 0; i < rate; i++)
        {
            await limiter.AcquireAsync(ZoomRateClass.Heavy);
        }

        await limiter.AcquireAsync(ZoomRateClass.Heavy);

        var wait = Assert.Single(waits);
        Assert.InRange(wait.TotalSeconds, 0.05, 1.0 / rate + 0.01);
    }

    [Fact]
    public async Task Classes_are_limited_independently()
    {
        var (limiter, _, waits) = Build();
        for (var i = 0; i < ZoomOutboundLimiter.PerSecond(ZoomRateClass.Heavy); i++)
        {
            await limiter.AcquireAsync(ZoomRateClass.Heavy);
        }

        await limiter.AcquireAsync(ZoomRateClass.Light);

        Assert.Empty(waits);
    }

    [Fact]
    public async Task A_penalty_from_a_429_blocks_the_class_until_retry_after_has_passed()
    {
        var (limiter, clock, waits) = Build();
        limiter.Penalize(ZoomRateClass.Medium, TimeSpan.FromSeconds(5));

        await limiter.AcquireAsync(ZoomRateClass.Medium);

        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(waits));
        Assert.True(clock.UtcNow >= new DateTime(2027, 1, 10, 9, 0, 5, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_penalty_on_one_class_leaves_the_others_alone()
    {
        var (limiter, _, waits) = Build();
        limiter.Penalize(ZoomRateClass.Heavy, TimeSpan.FromSeconds(5));

        await limiter.AcquireAsync(ZoomRateClass.Light);

        Assert.Empty(waits);
    }

    [Fact]
    public async Task The_queue_is_bounded_and_an_overflowing_caller_is_refused()
    {
        var clock = new ManualClock(DateTime.UtcNow);
        var gate = new TaskCompletionSource();
        var limiter = new ZoomOutboundLimiter(clock, (_, _) => gate.Task, maxQueue: 2);
        limiter.Penalize(ZoomRateClass.Light, TimeSpan.FromMinutes(1));

        var waiting = new[] { limiter.AcquireAsync(ZoomRateClass.Light), limiter.AcquireAsync(ZoomRateClass.Light) };
        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() => limiter.AcquireAsync(ZoomRateClass.Light));

        Assert.Equal(ZoomFailure.RateLimited, ex.Failure);
        clock.Advance(TimeSpan.FromMinutes(2)); // time passes first, so the released waiters find the penalty over
        gate.SetResult();
        await Task.WhenAll(waiting);
    }

    [Theory]
    [InlineData("GET", "users/h1", ZoomRateClass.Light)]
    [InlineData("POST", "users/h1/meetings", ZoomRateClass.Light)]
    [InlineData("PATCH", "meetings/123", ZoomRateClass.Light)]
    [InlineData("DELETE", "meetings/123", ZoomRateClass.Light)]
    [InlineData("GET", "report/meetings/123/participants", ZoomRateClass.Heavy)]
    [InlineData("GET", "meetings/123/recordings", ZoomRateClass.Medium)]
    public void Requests_are_classified_by_method_and_path(string method, string path, ZoomRateClass expected) =>
        Assert.Equal(expected, ZoomOutboundLimiter.Classify(new HttpMethod(method), path));
}
