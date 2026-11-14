using Lms.Domain.Packages;
using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages.Attempts;

/// <summary>
/// The adapter used until the SCORM and H5P adapters of Tasks 10 and 11 are registered: it serves <c>index.html</c> and never judges a
/// content complete or scored, so no completion or grade is ever invented from a data model nobody interpreted. A kind-specific
/// <see cref="IPackageTracker"/> registered later wins (the last registration of a kind is used).
/// </summary>
public sealed class PlaceholderPackageTracker : IPackageTracker
{
    public string Kind => "*";

    public string LaunchPath(PackageUpload package) => "index.html";

    public TrackingJudgement Judge(PackageUpload package, string completionRule, decimal? minScore, IReadOnlyDictionary<string, string> model) =>
        new(false, null, null, null);
}
