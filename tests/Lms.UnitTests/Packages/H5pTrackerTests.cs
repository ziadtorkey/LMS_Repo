using Lms.Domain.Packages;
using Lms.Infrastructure.Packages.H5p;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>M4b Task 11: how the H5P adapter reads the xAPI result an H5P content reports (ADR 0005, D57).</summary>
public sealed class H5pTrackerTests
{
    private static readonly PackageUpload Package = (PackageUpload)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PackageUpload));

    private static IReadOnlyDictionary<string, string> Model(params (string Key, string Value)[] data) => data.ToDictionary(d => d.Key, d => d.Value);

    private static TrackingJudgement Judge(string rule, decimal? min, params (string Key, string Value)[] data) =>
        new H5pTracker().Judge(Package, rule, min, Model(data));

    [Fact]
    public void The_adapter_serves_h5p_and_launches_the_generated_page()
    {
        var tracker = new H5pTracker();

        Assert.Equal(PackageKinds.H5p, tracker.Kind);
        Assert.Equal(H5pTracker.LaunchFile, tracker.LaunchPath(Package));
        Assert.DoesNotContain("..", H5pTracker.LaunchFile, StringComparison.Ordinal);
    }

    [Fact]
    public void Xapi_result_maps_to_score_and_completion_under_the_score_rule()
    {
        var passed = Judge(CompletionRules.Score, 60m, ("verb", "completed"), ("result.completion", "true"), ("result.score.raw", "8"), ("result.score.min", "0"), ("result.score.max", "10"));

        Assert.True(passed.Completed); // 80 % of the maximum is above the minimum of 60
        Assert.Equal((8m, 0m, 10m), (passed.ScoreRaw, passed.ScoreMin, passed.ScoreMax));

        var failed = Judge(CompletionRules.Score, 90m, ("verb", "completed"), ("result.completion", "true"), ("result.score.raw", "8"), ("result.score.max", "10"));
        Assert.False(failed.Completed); // below the minimum: the score is recorded, the module is not complete
        Assert.Equal(8m, failed.ScoreRaw);
    }

    [Fact]
    public void A_scaled_score_stands_in_for_a_missing_raw_one_and_a_success_flag_decides_without_a_minimum()
    {
        var scaled = Judge(CompletionRules.Score, 50m, ("result.score.scaled", "0.75"));
        Assert.Equal((75m, 0m, 100m), (scaled.ScoreRaw, scaled.ScoreMin ?? 0m, scaled.ScoreMax));
        Assert.True(scaled.Completed);

        Assert.True(Judge(CompletionRules.Score, null, ("result.success", "true"), ("result.score.raw", "3"), ("result.score.max", "10")).Completed);
        Assert.False(Judge(CompletionRules.Score, null, ("result.success", "false"), ("result.score.raw", "9"), ("result.score.max", "10")).Completed);
        Assert.False(Judge(CompletionRules.Score, 50m).Completed); // no result at all
    }

    [Fact]
    public void The_submit_rule_needs_a_completion_and_never_a_score()
    {
        Assert.True(Judge(CompletionRules.Submit, null, ("result.completion", "true")).Completed);
        Assert.True(Judge(CompletionRules.Submit, null, ("verb", "answered")).Completed);
        Assert.False(Judge(CompletionRules.Submit, null, ("verb", "attempted"), ("result.completion", "false")).Completed);
        Assert.False(Judge(CompletionRules.Submit, null).Completed);
    }

    [Fact]
    public void Nonsense_numbers_are_dropped_not_trusted()
    {
        var odd = Judge(CompletionRules.Score, 10m, ("result.score.raw", "NaN"), ("result.score.max", "-5"), ("result.score.scaled", "7"));

        Assert.Null(odd.ScoreRaw); // not a number, a negative maximum and a scaled score outside -1..1 are all ignored
        Assert.Null(odd.ScoreMax);
        Assert.False(odd.Completed);

        var huge = Judge(CompletionRules.Score, 10m, ("result.score.raw", "1e12"), ("result.score.max", "10"));
        Assert.Null(huge.ScoreRaw);
    }

    [Fact]
    public void Raw_above_the_maximum_is_capped_at_the_maximum()
    {
        var capped = Judge(CompletionRules.Score, 50m, ("result.score.raw", "15"), ("result.score.max", "10"));

        Assert.Equal(10m, capped.ScoreRaw);
        Assert.Equal(10m, capped.ScoreMax);
        Assert.True(capped.Completed);
    }

    [Fact]
    public void Raw_is_clamped_into_zero_and_the_maximum_and_the_maximum_defaults_to_100()
    {
        var huge = Judge(CompletionRules.Score, 60m, ("result.score.raw", "999999"));
        Assert.Equal(100m, huge.ScoreRaw); // no maximum reported: 100 is the ceiling

        var negative = Judge(CompletionRules.Score, 60m, ("result.score.raw", "-500"));
        Assert.Equal(0m, negative.ScoreRaw);
        Assert.False(negative.Completed);

        var overMax = Judge(CompletionRules.Score, 60m, ("result.score.raw", "50"), ("result.score.max", "10"));
        Assert.Equal(10m, overMax.ScoreRaw);
        Assert.Equal(0m, Judge(CompletionRules.Score, 60m, ("result.score.scaled", "-0.5")).ScoreRaw); // a negative scaled score is 0
    }
}
