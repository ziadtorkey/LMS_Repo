using Lms.Domain.Abstractions;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Configuration;

public sealed class PolicyEvaluator(LmsDbContext db, ICurrentContext context) : IPolicyEvaluator
{
    public async Task<Guid?> FindBlockingUnitAsync(
        string permission, string orgUnitPath, CancellationToken cancellationToken = default)
    {
        var policyKeys = PolicyCatalog.BlockedPermissions
            .Where(p => p.Value.Contains(permission))
            .Select(p => p.Key)
            .ToList();
        if (policyKeys.Count == 0 || (context.OrganizationId is null && !context.IsPlatformAdmin))
        {
            return null;
        }

        var paths = OrgUnitPath.AncestorsAndSelf(orgUnitPath);
        var organizationId = context.OrganizationId;

        // ISOLATION-BYPASS: policies sit on ancestors outside the scope of the caller. Soft-delete and client
        // filters are re-applied by hand (platform admins have no client; paths are globally unique unit ids).
        return await db.Set<OrgUnitPolicy>().IgnoreQueryFilters()
            .Where(x => !x.IsDeleted && policyKeys.Contains(x.PolicyKey) && paths.Contains(x.OrgUnitPath)
                && (organizationId == null || x.OrganizationId == organizationId))
            .OrderBy(x => x.OrgUnitPath.Length)
            .Select(x => (Guid?)x.OrgUnitId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
