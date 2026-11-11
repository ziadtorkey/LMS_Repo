using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.OrgUnits;

/// <summary>
/// Moves a unit under a new parent (FR-ORG-03). Refused: the root, the unit under itself or a descendant, another client,
/// a parent whose level is not strictly shallower. The unit changes through EF (so the move is audited with the old and
/// new parent) and every stored path below it is rewritten in the same transaction.
/// </summary>
public sealed class MoveOrgUnitHandler(LmsDbContext db, IOrgUnitRepather repather)
{
    public async Task<IResult?> HandleAsync(OrgUnit unit, OrgUnit newParent, CancellationToken ct)
    {
        if (unit.ParentId is null || newParent.Id == unit.Id || newParent.OrganizationId != unit.OrganizationId
            || OrgUnitPath.IsWithin(newParent.Path, unit.Path))
        {
            return ApiProblems.Conflict(ProblemCodes.invalid_move);
        }

        var unitLevel = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == unit.LevelTypeId, ct);
        var parentLevel = await db.LevelTypes.FirstOrDefaultAsync(l => l.Id == newParent.LevelTypeId, ct);
        if (unitLevel is null || parentLevel is null || parentLevel.Order >= unitLevel.Order)
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_level_type);
        }

        if (unit.ParentId == newParent.Id)
        {
            return null; // already there
        }

        var oldPath = unit.Path;
        var oldDepth = unit.Depth;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        unit.MoveUnder(newParent);
        await db.SaveChangesAsync(ct);
        await repather.RepathAsync(unit.OrganizationId, oldPath, unit.Path, unit.Depth - oldDepth, ct);
        await transaction.CommitAsync(ct);
        return null;
    }
}