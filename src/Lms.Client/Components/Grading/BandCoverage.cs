namespace Lms.Client.Components.Grading;

/// <summary>What is wrong with the way a scale's bands cover 0 to 100. A hint only: the server decides (<c>scale_bands_invalid</c>).</summary>
public enum CoverageIssueKind
{
    StartsAbove,
    EndsBelow,
    Gap,
    Overlap,
    Inverted,
}

public sealed record CoverageIssue(CoverageIssueKind Kind, decimal From, decimal To);

public static class BandCoverage
{
    /// <summary>Bands may touch (next min = previous max) or follow in steps of 0.01 (89.99 then 90).</summary>
    private const decimal Step = 0.01m;

    public static IReadOnlyList<CoverageIssue> Analyse(IEnumerable<(decimal Min, decimal Max)> bands)
    {
        var sorted = bands.OrderBy(b => b.Min).ThenBy(b => b.Max).ToList();
        var issues = new List<CoverageIssue>();
        if (sorted.Count == 0) return [new CoverageIssue(CoverageIssueKind.StartsAbove, 0m, 100m)];
        if (sorted[0].Min > 0m) issues.Add(new CoverageIssue(CoverageIssueKind.StartsAbove, 0m, sorted[0].Min));
        foreach (var b in sorted.Where(b => b.Max < b.Min)) issues.Add(new CoverageIssue(CoverageIssueKind.Inverted, b.Min, b.Max));
        for (var i = 1; i < sorted.Count; i++)
        {
            var previous = sorted[i - 1].Max;
            var next = sorted[i].Min;
            if (next > previous + Step) issues.Add(new CoverageIssue(CoverageIssueKind.Gap, previous, next));
            else if (next < previous) issues.Add(new CoverageIssue(CoverageIssueKind.Overlap, next, previous));
        }

        if (sorted[^1].Max < 100m) issues.Add(new CoverageIssue(CoverageIssueKind.EndsBelow, sorted[^1].Max, 100m));
        return issues;
    }
}
