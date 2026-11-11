using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.OrgUnits;

/// <summary>A unit that another unit names as its next-level transfer target cannot be deleted (it would leave a dangling reference).</summary>
public sealed class TransferTargetUsageCheck(LmsDbContext db) : IOrgUnitUsageCheck
{
    public async Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken cancellationToken)
    {
        // The referencing unit can sit outside the caller's scope, so the scope filter must not hide it.
        // ISOLATION-BYPASS: existence check only; the client and soft-delete filters are re-applied explicitly.
        var referenced = await db.OrgUnits.IgnoreQueryFilters()
            .AnyAsync(u => u.OrganizationId == unit.OrganizationId && !u.IsDeleted && u.NextTransferUnitId == unit.Id, cancellationToken);
        return referenced ? "unit_is_transfer_target" : null;
    }
}
