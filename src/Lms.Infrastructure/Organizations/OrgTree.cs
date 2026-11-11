using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Organizations;

public sealed class OrgTree(LmsDbContext db) : IOrgTree
{
    public Task<OrgUnit?> FindVisibleAsync(Guid orgUnitId, CancellationToken cancellationToken) =>
        db.OrgUnits.FirstOrDefaultAsync(u => u.Id == orgUnitId, cancellationToken);

    public async Task<OrgUnit> GetRootAsync(Guid organizationId, CancellationToken cancellationToken) =>
        // ISOLATION-BYPASS: the client root lies above the scope of a unit-level caller; the client is filtered
        // explicitly and soft-deleted units are excluded by hand.
        await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId && !u.IsDeleted && u.ParentId == null)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException($"Organization {organizationId} has no root unit.");

    public async Task<IReadOnlyList<OrgUnit>> GetAncestorsAndSelfAsync(
        string path, Guid organizationId, CancellationToken cancellationToken)
    {
        var paths = OrgUnitPath.AncestorsAndSelf(path);

        // ISOLATION-BYPASS: ancestors lie above the caller's scope; the client is filtered explicitly and
        // soft-deleted units are excluded by hand.
        return await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => u.OrganizationId == organizationId && !u.IsDeleted && paths.Contains(u.Path))
            .OrderBy(u => u.Depth)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> CodeExistsAsync(Guid organizationId, string code, Guid? exceptUnitId, CancellationToken cancellationToken) =>
        // ISOLATION-BYPASS: codes are unique per client, so a unit in a subtree the caller cannot see still counts;
        // the client is filtered explicitly and deleted units are excluded by hand.
        db.OrgUnits.IgnoreQueryFilters().AnyAsync(
            u => u.OrganizationId == organizationId && !u.IsDeleted && u.Code == code && u.Id != exceptUnitId,
            cancellationToken);
}