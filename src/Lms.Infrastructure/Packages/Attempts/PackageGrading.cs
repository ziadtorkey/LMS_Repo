using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.Attempts;

/// <summary>One completed attempt as the grading rule sees it.</summary>
public sealed record GradedAttempt(Guid Id, DateTime CompletedAtUtc, decimal? ScoreRaw, decimal? ScoreMax);

/// <summary>
/// Which completed attempt the gradebook holds (D57), the same rule for a new attempt and for a change of the method: <c>Highest</c> keeps
/// the best score (another attempt never lowers it), <c>Last</c> the newest completed attempt, <c>First</c> the first one (later
/// attempts are practice). Scores compare as a share of their maximum (100 when the package reports none); an attempt without a score
/// never wins <c>Highest</c> over one with a score.
/// </summary>
public static class PackageGrading
{
    public const decimal DefaultMax = 100m;

    public static GradedAttempt? Pick(string method, IReadOnlyCollection<GradedAttempt> completed)
    {
        if (completed.Count == 0)
        {
            return null;
        }

        var byTime = completed.OrderBy(a => a.CompletedAtUtc).ThenBy(a => a.Id).ToList();
        return method switch
        {
            PackageGradingMethods.Last => byTime[^1],
            PackageGradingMethods.First => byTime[0],
            _ => byTime.Where(a => a.ScoreRaw is not null).OrderByDescending(Share).ThenBy(a => a.CompletedAtUtc).FirstOrDefault() ?? byTime[^1],
        };
    }

    public static decimal Share(GradedAttempt a) => (a.ScoreRaw ?? 0m) / (a.ScoreMax is > 0m ? a.ScoreMax.Value : DefaultMax);
}
