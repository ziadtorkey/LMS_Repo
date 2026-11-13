using Lms.Infrastructure.Jobs;

namespace Lms.UnitTests.Jobs;

public sealed class ScheduledJobKeysTests
{
    [Theory]
    [InlineData("live-reminder:0198a2b4-7c1e-7a55-9e11-4c2f6d0b8a33")]
    [InlineData("quiz-close/abc_123.v2")]
    [InlineData("a")]
    public void Plain_keys_are_valid(string key) => ScheduledJobKeys.Validate(key);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("quote'")]
    [InlineData("new\nline")]
    [InlineData("arabic-مفتاح")]
    public void Odd_keys_are_refused(string key) => Assert.Throws<ArgumentException>(() => ScheduledJobKeys.Validate(key));

    [Fact]
    public void A_key_longer_than_the_limit_is_refused_and_the_limit_itself_is_fine()
    {
        ScheduledJobKeys.Validate(new string('k', ScheduledJobKeys.MaxKeyLength));

        Assert.Throws<ArgumentException>(() => ScheduledJobKeys.Validate(new string('k', ScheduledJobKeys.MaxKeyLength + 1)));
    }

    [Fact]
    public void Storage_names_are_distinct_per_key_and_per_purpose()
    {
        Assert.NotEqual(ScheduledJobKeys.HashKey("a"), ScheduledJobKeys.HashKey("b"));
        Assert.NotEqual(ScheduledJobKeys.HashKey("a"), ScheduledJobKeys.LockKey("a"));
        Assert.StartsWith("lms:", ScheduledJobKeys.HashKey("a"));
    }

    [Fact]
    public void The_fingerprint_changes_with_the_arguments_or_the_context_and_nothing_else()
    {
        var same = ScheduledJobKeys.Fingerprint("Job.Run(1)", "{ctx}");

        Assert.Equal(same, ScheduledJobKeys.Fingerprint("Job.Run(1)", "{ctx}"));
        Assert.NotEqual(same, ScheduledJobKeys.Fingerprint("Job.Run(2)", "{ctx}"));
        Assert.NotEqual(same, ScheduledJobKeys.Fingerprint("Job.Run(1)", "{other}"));
        Assert.NotEqual(ScheduledJobKeys.Fingerprint("a\nb", "c"), ScheduledJobKeys.Fingerprint("a", "b\nc")); // no ambiguous joins
    }

    [Theory]
    [InlineData(null, true, ScheduleAction.Create)]
    [InlineData("Scheduled", true, ScheduleAction.Reschedule)]
    [InlineData("Scheduled", false, ScheduleAction.Replace)]
    [InlineData("Enqueued", true, ScheduleAction.Create)]
    [InlineData("Processing", true, ScheduleAction.Create)]
    [InlineData("Succeeded", false, ScheduleAction.Create)]
    [InlineData("Failed", true, ScheduleAction.Create)]
    [InlineData("Deleted", true, ScheduleAction.Create)]
    public void Only_a_waiting_job_is_moved_or_replaced(string? state, bool same, ScheduleAction expected) =>
        Assert.Equal(expected, ScheduledJobKeys.Decide(state, same));

    [Fact]
    public void The_mapping_lives_until_the_run_plus_the_grace_and_a_past_run_gets_only_the_grace()
    {
        Assert.Equal(TimeSpan.FromHours(2) + ScheduledJobKeys.MappingGrace, ScheduledJobKeys.MappingTtl(TimeSpan.FromHours(2)));
        Assert.Equal(ScheduledJobKeys.MappingGrace, ScheduledJobKeys.MappingTtl(TimeSpan.FromHours(-5)));
        Assert.Equal(ScheduledJobKeys.MappingGrace, ScheduledJobKeys.MappingTtl(TimeSpan.Zero));
    }
}
