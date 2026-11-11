using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.OrgUnits;

/// <summary>Delete rules (FR-ORG-04, D19): not the root, no live children, no live assignments, nothing registered uses it.</summary>
public sealed class DeleteOrgUnitHandler(
    LmsDbContext db, AssignmentUsageCheck assignments, IEnumerable<IOrgUnitUsageCheck> usageChecks)
{
    public async Task<IResult> HandleAsync(OrgUnit unit, CancellationToken ct)
    {
        if (unit.ParentId is null)
        {
            return ApiProblems.Conflict(ProblemCodes.root_unit);
        }

        if (await db.OrgUnits.AnyAsync(u => u.ParentId == unit.Id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.unit_has_children);
        }

        if (await assignments.HasAssignmentsAsync(unit.Id, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.unit_has_assignments);
        }

        foreach (var check in usageChecks)
        {
            if (await check.FindUsageAsync(unit, ct) is { } reason)
            {
                return ApiProblems.Conflict(ProblemCodes.unit_in_use, reason);
            }
        }

        db.OrgUnits.Remove(unit); // soft delete (stamping) + audit entry
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}