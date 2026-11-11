using Lms.Domain.Configuration;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Configuration;

public sealed class PolicyManager(LmsDbContext db) : IPolicyManager
{
    public async Task EnableAsync(Guid orgUnitId, string policyKey, CancellationToken cancellationToken = default)
    {
        // Through the filtered set: the caller can only manage policies inside its own scope.
        var unit = await db.OrgUnits.SingleOrDefaultAsync(x => x.Id == orgUnitId, cancellationToken)
            ?? throw new InvalidOperationException("Org unit not found in the current scope.");

        var existing = await FindIncludingDeletedAsync(unit.OrganizationId, orgUnitId, policyKey, cancellationToken);
        if (existing is null)
        {
            db.Add(new OrgUnitPolicy(unit.OrganizationId, unit.Id, unit.Path, policyKey));
        }
        else if (existing.IsDeleted)
        {
            existing.Restore();
        }
        else
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableAsync(Guid orgUnitId, string policyKey, CancellationToken cancellationToken = default)
    {
        var unit = await db.OrgUnits.SingleOrDefaultAsync(x => x.Id == orgUnitId, cancellationToken)
            ?? throw new InvalidOperationException("Org unit not found in the current scope.");

        var existing = await FindIncludingDeletedAsync(unit.OrganizationId, orgUnitId, policyKey, cancellationToken);
        if (existing is null || existing.IsDeleted)
        {
            return;
        }

        db.Remove(existing); // soft delete
        await db.SaveChangesAsync(cancellationToken);
    }

    // ISOLATION-BYPASS: only to see soft-deleted rows: the unique (OrgUnitId, PolicyKey) index still holds them, so
    // a re-enable must restore the old row instead of inserting. The unit was already resolved through the scoped
    // set, and the client is re-applied here explicitly.
    private Task<OrgUnitPolicy?> FindIncludingDeletedAsync(Guid organizationId, Guid orgUnitId, string key, CancellationToken ct) =>
        db.Set<OrgUnitPolicy>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.OrgUnitId == orgUnitId && x.PolicyKey == key, ct);
}
