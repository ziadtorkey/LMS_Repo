using Lms.Domain.Organizations;

namespace Lms.Infrastructure.Branches;

/// <summary>A setting defined at a copied unit.</summary>
public sealed record BranchSetting(string Name, string ValueJson, bool IsLocked);

/// <summary>One unit of a captured branch. <c>Key</c> 0 is the branch root; parents come before their children.</summary>
public sealed record BranchNode(
    int Key,
    int? ParentKey,
    Guid LevelTypeId,
    string NameAr,
    string NameEn,
    string Code,
    bool IsLevelTransfer,
    int? NextTransferKey,
    bool IsCourseManagementLevel,
    IReadOnlyList<string> PolicyKeys,
    IReadOnlyList<BranchSetting> Settings);

/// <summary>Plain, JSON-serializable copy of a branch's structure (and optionally its policies and settings).</summary>
public sealed record BranchSnapshot(IReadOnlyList<BranchNode> Nodes);

public sealed record MaterializeResult(OrgUnit NewRoot, int UnitsCreated, IReadOnlyList<string> SkippedLockedSettings);

/// <summary>The branch has more units than a snapshot may hold.</summary>
public sealed class BranchTooLargeException(int limit) : Exception($"A branch may hold at most {limit} units.");

/// <summary>Some codes of the copy are already taken in the client; nothing was created.</summary>
public sealed class BranchCodeConflictException(IReadOnlyList<string> codes)
    : Exception("Some codes are already in use.")
{
    public IReadOnlyList<string> Codes { get; } = codes;
}

/// <summary>The snapshot cannot be placed below the parent: its level types do not fit, or a code is invalid.</summary>
public sealed class BranchInvalidException(string message) : Exception(message);

public interface IBranchSnapshotter
{
    /// <summary>
    /// Captures the live units below <paramref name="root"/> (itself included) that the caller can see. Transfer pointers
    /// leaving the branch are dropped.
    /// </summary>
    /// <exception cref="BranchTooLargeException">More than 500 units.</exception>
    Task<BranchSnapshot> CaptureAsync(OrgUnit root, bool includePolicies, bool includeSettings, CancellationToken cancellationToken);
}

public interface IBranchMaterializer
{
    /// <summary>
    /// Creates the snapshot below <paramref name="parent"/> in one transaction: new ids, remapped transfer pointers,
    /// policies and settings written at the new units. A setting locked by <paramref name="parent"/> or an ancestor is
    /// skipped and reported. Does not authorize: the caller checks permissions on the parent first.
    /// </summary>
    /// <exception cref="BranchCodeConflictException">Codes already taken; nothing created.</exception>
    /// <exception cref="BranchInvalidException">Level types do not fit below the parent.</exception>
    Task<MaterializeResult> MaterializeAsync(
        BranchSnapshot snapshot, OrgUnit parent, Func<string, string> transformName, Func<string, string> transformCode,
        CancellationToken cancellationToken);
}
