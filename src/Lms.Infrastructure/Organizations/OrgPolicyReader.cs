using Lms.Domain.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Organizations;

/// <summary>A policy switched on at <see cref="Path"/>, resolved to the permissions it blocks in that subtree.</summary>
public sealed record PolicyBlock(string Path, IReadOnlyList<string> BlockedPermissions);

/// <summary>Reads the live policies of clients for the org tree (one query), mapped through the PolicyCatalog.</summary>
public interface IOrgPolicyReader
{
    Task<IReadOnlyList<PolicyBlock>> LoadAsync(IReadOnlyList<Guid> organizationIds, CancellationToken cancellationToken);
}

public sealed class OrgPolicyReader(LmsDbContext db) : IOrgPolicyReader
{
    public async Task<IReadOnlyList<PolicyBlock>> LoadAsync(IReadOnlyList<Guid> organizationIds, CancellationToken cancellationToken)
    {
        // ISOLATION-BYPASS: policies sit on ancestors above the caller's scope; the clients are matched explicitly
        // and deleted rows are excluded by hand.
        var rows = await db.Set<OrgUnitPolicy>().IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && organizationIds.Contains(p.OrganizationId))
            .Select(p => new { p.OrgUnitPath, p.PolicyKey })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => PolicyCatalog.BlockedPermissions.ContainsKey(r.PolicyKey))
            .Select(r => new PolicyBlock(r.OrgUnitPath, PolicyCatalog.BlockedPermissions[r.PolicyKey]))
            .ToList();
    }
}