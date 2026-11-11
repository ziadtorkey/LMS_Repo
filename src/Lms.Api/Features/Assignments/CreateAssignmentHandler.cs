using System.Net.Mail;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Assignments;

/// <summary>
/// POST /api/assignments: assign a role at a unit by email (D21). The email must belong to an existing staff
/// member in the caller's scope (M2 Task 9, D31): staff are created through POST /api/staff, never as a side effect here.
/// </summary>
public sealed class CreateAssignmentHandler(
    LmsDbContext db, IClock clock, CallerPermissions caller, AssignmentNotifier notifier, IRoleGrantGuard roleGuard)
{
    private const int MaxEmailLength = 256;

    public async Task<IResult> HandleAsync(CreateAssignmentRequest request, HttpContext http, CancellationToken ct)
    {
        // The unit decides who may assign: a unit outside the caller's scope is 404, a policy-blocked one is 403.
        var access = await http.CheckUnitAsync(Permissions.Assignments.Create, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length is 0 or > MaxEmailLength || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            return AssignmentQueries.Invalid(nameof(request.Email), "email_invalid");
        }

        if (request.ValidFrom is { } from && request.ValidTo is { } to && to < from)
        {
            return AssignmentQueries.Invalid(nameof(request.ValidTo), "validity_range_invalid");
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.RoleId && r.OrganizationId == unit.OrganizationId, ct);
        if (role is null)
        {
            return ApiProblems.NotFound();
        }

        // Student and guardian assignments are managed by the people services, never by hand.
        if (role.BaseKind != RoleBaseKind.Staff)
        {
            return ApiProblems.BadRequest(ProblemCodes.invalid_role_kind);
        }

        if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, role.Id, ct))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.permission_escalation });
        }

        var grant = await roleGuard.CheckAsync(role, unit, ct);
        if (!grant.Allowed)
        {
            return AccessProblem.From(grant, http);
        }

        // Roles go to existing staff only (staff are created through POST /api/staff). The person must have a live staff
        // profile in the caller's scope: an unknown email and one that belongs to a sibling subtree look the same (400),
        // so nothing outside the scope is revealed and no user is created here.
        var normalized = PersonNormalizer.Email(email)!;
        var profile = await db.Set<StaffProfile>().FirstOrDefaultAsync(p => p.Email == normalized, ct);
        var user = profile is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == profile.UserId, ct);
        if (user is null)
        {
            return ApiProblems.BadRequest(ProblemCodes.person_not_found);
        }

        if (await AssignmentQueries.OverlapsAsync(db, user.Id, role.Id, unit.Id, request.ValidFrom, request.ValidTo, null, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.assignment_exists);
        }

        var assignment = new RoleAssignment(unit, user.Id, role.Id, request.ValidFrom, request.ValidTo);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        await notifier.RoleAssignedAsync(assignment, role, unit, ct);

        return await AssignmentQueries.GetAsync(db, clock, assignment.Id, ct) is { } dto
            ? Results.Created(Routes.Assignment(assignment.Id), dto)
            : ApiProblems.NotFound();
    }
}
