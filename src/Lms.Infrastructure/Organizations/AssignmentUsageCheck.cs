using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Organizations;

/// <summary>Whether a unit still has live role assignments (D19: such a unit cannot be deleted).</summary>
public sealed class AssignmentUsageCheck(LmsDbContext db)
{
    public Task<bool> HasAssignmentsAsync(Guid orgUnitId, CancellationToken cancellationToken) =>
        db.RoleAssignments.AnyAsync(a => a.OrgUnitId == orgUnitId, cancellationToken);
}