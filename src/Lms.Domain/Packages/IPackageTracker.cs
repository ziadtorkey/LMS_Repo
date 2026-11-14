namespace Lms.Domain.Packages;

/// <summary>What a format adapter makes of a data model: whether the module's completion rule is met and the score.</summary>
public sealed record TrackingJudgement(bool Completed, decimal? ScoreRaw, decimal? ScoreMin, decimal? ScoreMax);

/// <summary>
/// The format adapter between the data model a package reports and what the platform records (M4b Tasks 10 and 11 register one per
/// kind: SCORM and H5P). It sees only the data that passed the server's key allow-list and size checks, never a learner identity.
/// </summary>
public interface IPackageTracker
{
    /// <summary>The <c>PackageKinds</c> value this adapter serves.</summary>
    string Kind { get; }

    /// <summary>The entry page of the package, relative to its root (a path the host serves under the token).</summary>
    string LaunchPath(PackageUpload package);

    /// <summary>
    /// Judges the data model against the module's completion rule (<c>None</c>, <c>Submit</c> or <c>Score</c>) and its minimum score.
    /// Called after every commit (to keep the score current) and at <c>finish</c> (to decide completion).
    /// </summary>
    TrackingJudgement Judge(PackageUpload package, string completionRule, decimal? minScore, IReadOnlyDictionary<string, string> model);
}
