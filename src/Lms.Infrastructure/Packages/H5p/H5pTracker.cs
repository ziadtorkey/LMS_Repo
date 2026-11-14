using System.Globalization;
using Lms.Domain.Packages;
using Lms.Shared.Content;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.H5p;

/// <summary>
/// The H5P format adapter (M4b Task 11, ADR 0005, D57). An H5P content reports xAPI statements through the port; the page keeps only the result of the
/// statements that carry one (the flattened <c>verb</c> and <c>result.*</c> elements the commit allow-list admits), so the model this adapter reads is already
/// small and checked. A value that is not a plain number inside a sane range is ignored, never trusted: a content cannot fake a huge score. The adapter never sees
/// a learner identity. <b>Score rule:</b> complete when the score, as a share of the maximum, reaches the module minimum (without a minimum, when the content says it
/// was successful). <b>Submit and other rules:</b> complete when the content reports a completion or a <c>completed</c>, <c>answered</c> or <c>passed</c> verb.
/// </summary>
public sealed class H5pTracker : IPackageTracker
{
    /// <summary>The generated launch page the content host serves for an H5P package (it loads the vendored player and relays xAPI to the port).</summary>
    public const string LaunchFile = "__lms/h5p-launch.html";

    private const decimal Limit = 1_000_000m;
    private const decimal DefaultMax = 100m;

    private static readonly HashSet<string> DoneVerbs = new(StringComparer.OrdinalIgnoreCase) { "completed", "answered", "passed" };

    public string Kind => PackageKinds.H5p;

    public string LaunchPath(PackageUpload package) => LaunchFile;

    public TrackingJudgement Judge(PackageUpload package, string completionRule, decimal? minScore, IReadOnlyDictionary<string, string> model)
    {
        var (raw, min, max) = Score(model);
        var completionFlag = Is(model, "result.completion", "true") || model.TryGetValue("verb", out var verb) && DoneVerbs.Contains(verb);
        bool completed;
        if (completionRule == CompletionRules.Score)
        {
            completed = minScore is { } required
                ? raw is { } r && r / (max ?? DefaultMax) * 100m >= required
                : Is(model, "result.success", "true") || !Is(model, "result.success", "false") && Is(model, "result.completion", "true") && raw is not null;
        }
        else
        {
            completed = completionFlag;
        }

        return new TrackingJudgement(completed, raw, min, max);
    }

    private static (decimal? Raw, decimal? Min, decimal? Max) Score(IReadOnlyDictionary<string, string> model)
    {
        var max = Number(model, "result.score.max") is { } m and > 0m ? m : (decimal?)null;
        var min = Number(model, "result.score.min");
        var raw = Number(model, "result.score.raw");
        if (raw is null && Number(model, "result.score.scaled") is { } scaled and >= -1m and <= 1m)
        {
            return (scaled * DefaultMax, 0m, DefaultMax); // a scaled score (-1..1) read on the 0..100 scale
        }

        if (raw is { } value && max is { } ceiling && value > ceiling)
        {
            raw = ceiling; // never above the maximum
        }

        return (raw, min, max);
    }

    private static bool Is(IReadOnlyDictionary<string, string> model, string key, string expected) =>
        model.TryGetValue(key, out var value) && string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static decimal? Number(IReadOnlyDictionary<string, string> model, string key) =>
        model.TryGetValue(key, out var text) && decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && Math.Abs(number) <= Limit ? number : null;
}
