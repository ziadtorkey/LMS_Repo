using Lms.Api.Features.Assignments;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>
/// Sharing a staff member into other units (FR-STF-05, D29): it is just more role assignments, so the usual rules apply per
/// target (permission at the unit, no escalation, role-blocking policies); the owner of the profile must also be allowed to
/// update it. All targets succeed or none does.
/// </summary>
public sealed class ShareStaffHandler(
    LmsDbContext db, ICurrentContext context, CallerPermissions caller, IRoleGrantGuard roleGuard, AssignmentNotifier notifier)
{
    public async Task<IResult> ShareAsync(Guid id, ShareStaffRequest? request, HttpContext http, CancellationToken ct)
    {
        var inputs = request?.Targets;
        if (inputs is not { Count: > 0 } || inputs.Any(r => r is null)
            || inputs.Select(r => (r.RoleId, r.OrgUnitId)).Distinct().Count() != inputs.Count
            || inputs.Any(r => r.ValidFrom is { } from && r.ValidTo is { } to && to < from))
        {
            return AssignmentQueries.Invalid(nameof(ShareStaffRequest.Targets), "roles_required");
        }

        // Filtered set: a person whose home is outside the caller's scope simply does not exist for the caller. A platform
        // admin without a client context acts on no client's staff.
        var profile = context.OrganizationId is null ? null : await db.Set<StaffProfile>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return ApiProblems.NotFound();
        }

        var owner = await http.CheckUnitAsync(Permissions.Staff.Update, profile.OrgUnitId, ct);
        if (owner.Problem is not null)
        {
            return owner.Problem;
        }

        var created = new List<(RoleAssignment Assignment, Role Role, OrgUnit Unit)>();
        foreach (var input in inputs)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == input.RoleId && r.OrganizationId == profile.OrganizationId, ct);
            if (role is null)
            {
                return ApiProblems.NotFound();
            }

            if (role.BaseKind != RoleBaseKind.Staff)
            {
                return ApiProblems.BadRequest(ProblemCodes.invalid_role_kind);
            }

            var target = await http.CheckUnitAsync(Permissions.Assignments.Create, input.OrgUnitId, ct);
            if (target.Problem is not null)
            {
                return target.Problem;
            }

            if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, role.Id, ct))
            {
                return Forbidden();
            }

            var grant = await roleGuard.CheckAsync(role, target.Unit!, ct);
            if (!grant.Allowed)
            {
                return AccessProblem.From(grant, http);
            }

            if (await AssignmentQueries.OverlapsAsync(db, profile.UserId, role.Id, target.Unit!.Id, input.ValidFrom, input.ValidTo, null, ct))
            {
                return ApiProblems.Conflict(ProblemCodes.assignment_exists);
            }

            created.Add((new RoleAssignment(target.Unit, profile.UserId, role.Id, input.ValidFrom, input.ValidTo), role, target.Unit));
        }

        db.RoleAssignments.AddRange(created.Select(c => c.Assignment)); // one SaveChanges: all or nothing
        await db.SaveChangesAsync(ct);
        foreach (var (assignment, role, unit) in created)
        {
            await notifier.RoleAssignedAsync(assignment, role, unit, ct);
        }

        return Results.Ok(created.Select(c => new StaffRoleDto(
            c.Assignment.Id, c.Role.Id, c.Role.NameAr, c.Role.NameEn, c.Unit.Id, c.Unit.NameAr, c.Unit.NameEn,
            c.Assignment.ValidFrom, c.Assignment.ValidTo,
            !c.Unit.Path.StartsWith(profile.OrgUnitPath, StringComparison.Ordinal))).ToList());
    }

    public async Task<IResult> UnshareAsync(Guid id, Guid assignmentId, HttpContext http, CancellationToken ct)
    {
        var organizationId = context.OrganizationId;
        // ISOLATION-BYPASS: the person's home may be outside the scope of the admin of the unit the share points at, who is
        // exactly the one who must be able to remove it. Limited to this client and live profiles; the filtered assignment
        // lookup below then proves the share itself lies inside the caller's scope.
        var profile = organizationId is null ? null : await db.Set<StaffProfile>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == organizationId && !p.IsDeleted, ct);
        var assignment = profile is null ? null
            : await db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId && a.UserId == profile.UserId, ct);
        if (assignment is null)
        {
            return ApiProblems.NotFound();
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == assignment.RoleId, ct);
        if (role is null || role.BaseKind != RoleBaseKind.Staff)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Assignments.Delete, assignment.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        // Nobody removes their own access: another admin must do it (no self lock-out).
        if (assignment.UserId == context.UserId)
        {
            return ApiProblems.Conflict(ProblemCodes.own_assignment);
        }

        if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, role.Id, ct))
        {
            return Forbidden();
        }

        db.RoleAssignments.Remove(assignment); // soft delete
        await db.SaveChangesAsync(ct);
        await notifier.RoleRemovedAsync(assignment, role, access.Unit!, ct);
        return Results.NoContent();
    }

    private static IResult Forbidden() =>
        Results.Problem(
            statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.permission_escalation });
}
