namespace Lms.Domain.Grading;

/// <summary>One band as the validator sees it (a save request or a stored band).</summary>
public sealed record ScaleBandInput(int Order, decimal MinPercent, decimal MaxPercent, string Code, decimal? Points);

/// <summary>A range of the 0-100 % line that no band covers, or that two bands cover, or another rule a band breaks.</summary>
public sealed record BandProblem(string Kind, decimal From, decimal To);

public sealed record BandValidation(IReadOnlyList<BandProblem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Bands cover 0-100 % with no gap and no overlap (FR-GRD-02): a band takes its minimum and stops before the next band's minimum, only the
/// last band includes 100. Each band needs a code, codes are unique, and a GPA scale gives every band its points. Pure: no I/O.
/// </summary>
public static class ScaleBandValidator
{
    public const string Gap = "Gap";
    public const string Overlap = "Overlap";
    public const string Range = "Range";
    public const string Code = "Code";
    public const string Points = "Points";
    public const string Count = "Count";

    public static BandValidation Validate(IReadOnlyList<ScaleBandInput> bands, bool requiresPoints, int maxBands)
    {
        var problems = new List<BandProblem>();
        if (bands.Count == 0 || bands.Count > maxBands)
        {
            problems.Add(new BandProblem(Count, 0, 100));
            return new BandValidation(problems);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var band in bands)
        {
            if (band.MinPercent < 0 || band.MaxPercent > 100 || band.MinPercent >= band.MaxPercent)
            {
                problems.Add(new BandProblem(Range, band.MinPercent, band.MaxPercent));
            }

            if (string.IsNullOrWhiteSpace(band.Code) || !seen.Add(band.Code.Trim()))
            {
                problems.Add(new BandProblem(Code, band.MinPercent, band.MaxPercent));
            }

            if (requiresPoints && band.Points is null or < 0)
            {
                problems.Add(new BandProblem(Points, band.MinPercent, band.MaxPercent));
            }
        }

        if (problems.Count > 0)
        {
            return new BandValidation(problems);
        }

        var covered = 0m;
        foreach (var band in bands.OrderBy(b => b.MinPercent).ThenBy(b => b.MaxPercent))
        {
            if (band.MinPercent > covered)
            {
                problems.Add(new BandProblem(Gap, covered, band.MinPercent));
            }
            else if (band.MinPercent < covered)
            {
                problems.Add(new BandProblem(Overlap, band.MinPercent, Math.Min(covered, band.MaxPercent)));
            }

            covered = Math.Max(covered, band.MaxPercent);
        }

        if (covered < 100)
        {
            problems.Add(new BandProblem(Gap, covered, 100));
        }

        return new BandValidation(problems);
    }

    /// <summary>The band a percentage falls in: the one with the highest minimum at or below it (minimum inclusive; 100 is in the last band).</summary>
    public static T? Find<T>(IEnumerable<T> bands, decimal percent, Func<T, decimal> min) where T : class =>
        bands.Where(b => min(b) <= percent).OrderByDescending(min).FirstOrDefault();
}
