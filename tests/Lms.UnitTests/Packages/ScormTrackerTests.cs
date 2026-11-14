using Lms.Domain.Packages;
using Lms.Infrastructure.Packages.Scorm;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>The SCORM adapter: data model to completion and score (M4b Task 10; grading per D57 is the attempt service's).</summary>
public sealed class ScormTrackerTests
{
    private static PackageUpload Package(string version, string? launch = null)
    {
        var upload = (PackageUpload)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PackageUpload));
        typeof(PackageUpload).GetProperty(nameof(PackageUpload.Version))!.SetValue(upload, version);
        typeof(PackageUpload).GetProperty(nameof(PackageUpload.LaunchPath))!.SetValue(upload, launch);
        return upload;
    }

    private static Dictionary<string, string> M(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private readonly ScormTracker _tracker = new();

    [Theory]
    [InlineData("completed", true)]
    [InlineData("passed", true)]
    [InlineData("failed", true)]
    [InlineData("incomplete", false)]
    [InlineData("browsed", false)]
    [InlineData("not attempted", false)]
    [InlineData("COMPLETED", true)]
    [InlineData("", false)]
    [InlineData("garbage", false)]
    public void Scorm12_lesson_status_decides_submit(string status, bool completed)
    {
        var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.Submit, null, M(("cmi.core.lesson_status", status)));

        Assert.Equal(completed, judged.Completed);
    }

    [Theory]
    [InlineData("completed", "unknown", true)]
    [InlineData("incomplete", "passed", true)]
    [InlineData("incomplete", "failed", true)]
    [InlineData("incomplete", "unknown", false)]
    [InlineData("not attempted", "unknown", false)]
    [InlineData("unknown", "unknown", false)]
    public void Scorm2004_completion_and_success_status_decide_submit(string completion, string success, bool completed)
    {
        var judged = _tracker.Judge(Package("Scorm2004"), CompletionRules.Submit, null,
            M(("cmi.completion_status", completion), ("cmi.success_status", success)));

        Assert.Equal(completed, judged.Completed);
    }

    [Fact]
    public void Rule_none_never_completes_but_the_score_is_kept()
    {
        var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.None, null,
            M(("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "80"), ("cmi.core.score.max", "100")));

        Assert.False(judged.Completed);
        Assert.Equal((80m, 100m), (judged.ScoreRaw, judged.ScoreMax));
    }

    [Theory]
    [InlineData("completed", "70", "100", 60, true)]
    [InlineData("completed", "59", "100", 60, false)]
    [InlineData("completed", "6", "10", 60, true)]
    [InlineData("completed", "5", "10", 60, false)]
    [InlineData("completed", "60", null, 60, true)]
    [InlineData("incomplete", "90", "100", 60, false)]
    [InlineData("completed", null, null, 60, false)]
    [InlineData("passed", null, null, null, true)]
    [InlineData("completed", null, null, null, false)]
    [InlineData("failed", "90", "100", 60, true)]
    public void Scorm12_score_rule_compares_a_percentage_with_the_minimum(string status, string? raw, string? max, int? minScore, bool completed)
    {
        var model = M(("cmi.core.lesson_status", status));
        if (raw is not null)
        {
            model["cmi.core.score.raw"] = raw;
        }

        if (max is not null)
        {
            model["cmi.core.score.max"] = max;
        }

        var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.Score, minScore, model);

        Assert.Equal(completed, judged.Completed);
    }

    [Fact]
    public void Scorm2004_scaled_score_alone_is_read_as_a_percentage_and_a_negative_one_is_no_score()
    {
        var good = _tracker.Judge(Package("Scorm2004"), CompletionRules.Score, 70, M(("cmi.completion_status", "completed"), ("cmi.score.scaled", "0.85")));
        var low = _tracker.Judge(Package("Scorm2004"), CompletionRules.Score, 70, M(("cmi.completion_status", "completed"), ("cmi.score.scaled", "0.5")));
        var negative = _tracker.Judge(Package("Scorm2004"), CompletionRules.Score, 70, M(("cmi.completion_status", "completed"), ("cmi.score.scaled", "-0.5")));

        Assert.True(good.Completed);
        Assert.Equal((85m, 100m), (good.ScoreRaw, good.ScoreMax));
        Assert.False(low.Completed);
        Assert.False(negative.Completed);
        Assert.Null(negative.ScoreRaw);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1e400")]
    [InlineData("99999999999")]
    [InlineData("NaN")]
    public void A_score_that_is_not_a_sane_number_is_no_score(string raw)
    {
        var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.Score, 50,
            M(("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", raw)));

        Assert.Null(judged.ScoreRaw);
        Assert.False(judged.Completed);
    }

    [Theory]
    [InlineData("1000000", "100")]
    [InlineData("-500", "100")]
    [InlineData("100", "1")]
    [InlineData("50", "0")]
    [InlineData("50", "-10")]
    [InlineData("100.01", "100")]
    public void A_score_outside_zero_to_the_maximum_is_no_score_and_never_reaches_the_gradebook(string raw, string max)
    {
        var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.Score, 50,
            M(("cmi.core.lesson_status", "passed"), ("cmi.core.score.raw", raw), ("cmi.core.score.max", max)));

        Assert.Equal((null, null, null), (judged.ScoreRaw, judged.ScoreMin, judged.ScoreMax));
        Assert.False(judged.Completed);
    }

    [Fact]
    public void A_minimum_above_the_raw_score_or_below_zero_is_dropped_and_values_keep_two_decimals()
    {
        var bad = _tracker.Judge(Package("Scorm12"), CompletionRules.Submit, null,
            M(("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "40"), ("cmi.core.score.min", "60"), ("cmi.core.score.max", "100")));
        var negative = _tracker.Judge(Package("Scorm12"), CompletionRules.Submit, null,
            M(("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "40"), ("cmi.core.score.min", "-5")));
        var rounded = _tracker.Judge(Package("Scorm12"), CompletionRules.Submit, null,
            M(("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "66.66666"), ("cmi.core.score.max", "100.005")));

        Assert.Equal((40m, null, 100m), (bad.ScoreRaw, bad.ScoreMin, bad.ScoreMax));
        Assert.Null(negative.ScoreMin);
        Assert.Equal((66.67m, 100.01m), (rounded.ScoreRaw, rounded.ScoreMax));
    }

    [Fact]
    public void Scores_parse_with_a_decimal_point_whatever_the_server_culture()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("ar-EG");
        try
        {
            var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.Score, 80,
                M(("cmi.core.lesson_status", "completed"), ("cmi.core.score.raw", "85.5")));

            Assert.Equal(85.5m, judged.ScoreRaw);
            Assert.True(judged.Completed);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Keys_of_the_other_version_are_ignored()
    {
        var judged = _tracker.Judge(Package("Scorm12"), CompletionRules.Submit, null, M(("cmi.completion_status", "completed")));

        Assert.False(judged.Completed);
    }

    [Fact]
    public void The_launch_path_is_the_manifests_launch_file_or_index_html()
    {
        Assert.Equal("sco/start.html", _tracker.LaunchPath(Package("Scorm12", "sco/start.html")));
        Assert.Equal("index.html", _tracker.LaunchPath(Package("Scorm12")));
        Assert.Equal("Scorm", _tracker.Kind);
    }
}
