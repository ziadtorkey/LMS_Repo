using Lms.Domain.Abstractions;
using Lms.Infrastructure.Jobs;

namespace Lms.UnitTests.Jobs;

public sealed class JobSettingsTests
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UTC")]
    public void Time_zone_defaults_to_UTC(string? configured)
    {
        Assert.Equal(TimeSpan.Zero, JobSettings.Parse(configured).TimeZone.BaseUtcOffset);
    }

    [Fact]
    public void Configured_time_zone_is_used()
    {
        var settings = JobSettings.Parse("Asia/Riyadh");

        Assert.Equal(TimeSpan.FromHours(3), settings.TimeZone.BaseUtcOffset);
    }

    [Fact]
    public void Unknown_time_zone_fails_fast_with_the_option_name()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => JobSettings.Parse("Mars/Olympus"));

        Assert.Contains("Jobs:TimeZone", ex.Message);
    }

    [Fact]
    public void Today_follows_the_configured_zone_not_UTC()
    {
        var clock = new FixedClock(new DateTime(2040, 6, 15, 22, 30, 0, DateTimeKind.Utc)); // 01:30 on the 16th in Riyadh

        Assert.Equal(new DateOnly(2040, 6, 15), JobSettings.Default.Today(clock));
        Assert.Equal(new DateOnly(2040, 6, 16), JobSettings.Parse("Asia/Riyadh").Today(clock));
    }
}
