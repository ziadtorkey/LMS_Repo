using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.OrgUnits;

/// <summary>
/// PUT /api/org-units/{id}/academic-flags: the Level Transfer flag with its next-level mapping (FR-ORG-05) and the
/// course-management level flag (FR-ORG-09).
/// </summary>
public sealed class AcademicFlagsHandler(LmsDbContext db, IOrgTree tree, OrgUnitQueries queries)
{
    public async Task<IResult> HandleAsync(OrgUnit unit, OrgUnitAcademicFlagsRequest request, CancellationToken ct)
    {
        if (request.NextTransferUnitId is not null && !request.IsLevelTransfer)
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_transfer_target);
        }

        // Transfer units of the client with their mapping, whatever the caller's scope: a chain or a reference can
        // run through units the caller cannot see, and a cycle or a dangling target must still be caught.
        // ISOLATION-BYPASS: read-only chain check; the client and soft-delete filters are re-applied explicitly.
        var transferUnits = await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => u.OrganizationId == unit.OrganizationId && !u.IsDeleted && u.IsLevelTransfer)
            .Select(u => new { u.Id, u.LevelTypeId, u.NextTransferUnitId })
            .ToDictionaryAsync(u => u.Id, ct);

        if (!request.IsLevelTransfer && transferUnits.Values.Any(u => u.NextTransferUnitId == unit.Id))
        {
            return ApiProblems.Conflict(ProblemCodes.transfer_target_in_use);
        }

        if (request.NextTransferUnitId is { } targetId)
        {
            // A target the caller cannot see is reported as missing, like any other unit outside their scope.
            var target = await tree.FindVisibleAsync(targetId, ct);
            if (target is null)
            {
                return ApiProblems.NotFound();
            }

            if (target.Id == unit.Id
                || !transferUnits.TryGetValue(target.Id, out var mapped)
                || mapped.LevelTypeId != unit.LevelTypeId
                || LeadsBackTo(unit.Id, target.Id, transferUnits.ToDictionary(p => p.Key, p => p.Value.NextTransferUnitId)))
            {
                return ApiProblems.BadRequest(ProblemCodes.invalid_transfer_target);
            }
        }

        unit.SetAcademicFlags(request.IsLevelTransfer, request.NextTransferUnitId, request.IsCourseManagementLevel);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await queries.GetNodeAsync(unit, ct));
    }

    /// <summary>True when following the mapping from <paramref name="start"/> reaches <paramref name="unitId"/>.</summary>
    private static bool LeadsBackTo(Guid unitId, Guid start, IReadOnlyDictionary<Guid, Guid?> next)
    {
        var seen = new HashSet<Guid>();
        for (Guid? current = start; current is { } id; current = next.GetValueOrDefault(id))
        {
            if (id == unitId)
            {
                return true;
            }

            if (!seen.Add(id))
            {
                return false;
            }
        }

        return false;
    }
}
