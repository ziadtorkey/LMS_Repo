using System.Globalization;
using Lms.Domain.Packages;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.Scorm;

/// <summary>
/// The SCORM adapter of the attempt service (M4b Task 10, #427 7.7): it reads the data model a SCO reported and tells the service whether the
/// module's completion rule is met and what the score is; the service records completion and, per the module's grading method (D57), the
/// grade event. It sees only keys that passed the allow-list, never a learner identity.
/// <list type="bullet">
/// <item>SCORM 1.2: <c>cmi.core.lesson_status</c> (<c>completed</c>, <c>passed</c>, <c>failed</c>, <c>incomplete</c>, <c>browsed</c>,
/// <c>not attempted</c>) and <c>cmi.core.score.raw/min/max</c>.</item>
/// <item>SCORM 2004: <c>cmi.completion_status</c>, <c>cmi.success_status</c> and <c>cmi.score.raw/min/max</c> (a scaled score alone is
/// turned into a raw score out of 100).</item>
/// </list>
/// A SCO is <em>finished</em> when it reports completed, passed or failed. Rule <c>None</c> never completes the module (the score is still
/// kept); <c>Submit</c> completes it when the SCO is finished; <c>Score</c> when it is finished and the score, as a percentage of its maximum
/// (100 when none is reported), reaches the module's minimum score, or, when the module sets no minimum, when the SCO reports passed.
/// </summary>
public sealed class ScormTracker : IPackageTracker
{
    public string Kind => PackageKinds.Scorm;

    public string LaunchPath(PackageUpload package) => package.LaunchPath ?? "index.html";

    public TrackingJudgement Judge(PackageUpload package, string completionRule, decimal? minScore, IReadOnlyDictionary<string, string> model)
    {
        var is2004 = package.Version == ScormVersions.Scorm2004;
        var (raw, min, max) = Score(model, is2004);
        var passed = is2004 ? Is(model, "cmi.success_status", "passed") : Is(model, "cmi.core.lesson_status", "passed");
        var failed = is2004 ? Is(model, "cmi.success_status", "failed") : Is(model, "cmi.core.lesson_status", "failed");
        var completed = is2004 ? Is(model, "cmi.completion_status", "completed") : Is(model, "cmi.core.lesson_status", "completed");
        var finished = completed || passed || failed;

        var met = completionRule switch
        {
            CompletionRules.Submit => finished,
            CompletionRules.Score => finished && ScoreReached(raw, max, minScore, passed),
            _ => false,
        };
        return new TrackingJudgement(met, raw, min, max);
    }

    private static bool ScoreReached(decimal? raw, decimal? max, decimal? minScore, bool passed)
    {
        if (minScore is null)
        {
            return passed;
        }

        if (raw is null)
        {
            return false;
        }

        var percent = raw.Value / (max is > 0m ? max.Value : 100m) * 100m;
        return percent >= minScore.Value;
    }

    /// <summary>
    /// The score as the gradebook may see it. SCORM tracking is reported by the content, so nothing is taken on trust: the maximum must be positive
    /// (100 when none is reported), the raw score must lie between 0 and the maximum, the minimum (when given) between 0 and the raw score; a
    /// score that breaks any of these is no score at all, and the values are rounded to the two decimals the gradebook keeps.
    /// </summary>
    private static (decimal? Raw, decimal? Min, decimal? Max) Score(IReadOnlyDictionary<string, string> model, bool is2004)
    {
        var prefix = is2004 ? "cmi.score." : "cmi.core.score.";
        var raw = Number(model, prefix + "raw");
        var min = Number(model, prefix + "min");
        var max = Number(model, prefix + "max");
        if (raw is null && is2004 && Number(model, "cmi.score.scaled") is { } scaled && scaled is >= 0m and <= 1m)
        {
            // A SCO that reports only a scaled score (0..1): read it as a percentage; a negative or larger one is no score.
            return (Math.Round(scaled * 100m, 2, MidpointRounding.AwayFromZero), 0m, 100m);
        }

        if (raw is null || max is <= 0m || (max ?? 100m) is var ceiling && (raw < 0m || raw > ceiling))
        {
            return (null, null, null);
        }

        var cleanMin = min is { } m && m >= 0m && m <= raw ? Math.Round(m, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
        return (Math.Round(raw.Value, 2, MidpointRounding.AwayFromZero), cleanMin, max is null ? null : Math.Round(max.Value, 2, MidpointRounding.AwayFromZero));
    }

    private static bool Is(IReadOnlyDictionary<string, string> model, string key, string value) =>
        model.TryGetValue(key, out var found) && string.Equals(found.Trim(), value, StringComparison.OrdinalIgnoreCase);

    /// <summary>A finite number with an invariant decimal point, or null (the content wrote text, an empty string or an out-of-range value).</summary>
    private static decimal? Number(IReadOnlyDictionary<string, string> model, string key) =>
        model.TryGetValue(key, out var text) && decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && Math.Abs(number) <= 1_000_000m
            ? number
            : null;
}
