namespace Lms.Shared.Organizations;

/// <summary>
/// Body of POST /api/org-units/copy (FR-ORG-06): copies the branch under <c>SourceOrgUnitId</c> below
/// <c>TargetParentId</c>; every copied code gets <c>CodeSuffix</c>; the new root may be renamed.
/// </summary>
public sealed record CopyBranchRequest(
    Guid SourceOrgUnitId,
    Guid TargetParentId,
    string CodeSuffix,
    bool IncludePolicies,
    bool IncludeSettings,
    string? NewRootNameAr,
    string? NewRootNameEn);

/// <summary>Result of a branch copy; <c>SkippedLockedSettings</c> = setting names not copied because an ancestor locks them.</summary>
public sealed record CopyBranchResultDto(Guid NewRootId, int UnitsCreated, IReadOnlyList<string> SkippedLockedSettings);
