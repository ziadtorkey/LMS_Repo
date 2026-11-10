namespace Lms.Domain.Organizations;

/// <summary>Read access to a client's org tree for access checks.</summary>
public interface IOrgTree
{
    /// <summary>The unit if the caller can see it (normal tenant, subtree and soft-delete filters), else null.</summary>
    Task<OrgUnit?> FindVisibleAsync(Guid orgUnitId, CancellationToken cancellationToken);

    /// <summary>The root unit of a client, regardless of the caller's own scope.</summary>
    Task<OrgUnit> GetRootAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>The units on the path from the root to <paramref name="path"/> (inclusive), root first; live units of that client only.</summary>
    Task<IReadOnlyList<OrgUnit>> GetAncestorsAndSelfAsync(string path, Guid organizationId, CancellationToken cancellationToken);
}