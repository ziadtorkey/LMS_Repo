using Lms.Api.Features.Account;
using Lms.Api.Features.Assignments;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Files;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>
/// POST /api/staff: a profile, its login account (reused when the person already exists in another client) and the
/// first role assignments, in one transaction; the activation email goes out only after the commit (X-09).
/// </summary>
public sealed class CreateStaffHandler(
    LmsDbContext db, IClock clock, CallerPermissions caller, IPersonDuplicateFinder duplicates,
    IRoleGrantGuard roleGuard, StaffCreator creator, StaffQueries queries)
{
    public async Task<IResult> HandleAsync(CreateStaffRequest request, HttpContext http, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (StaffValidators.Validate(
                request.NameAr, request.NameEn, request.Email, true, request.Mobile, request.NationalId,
                request.JobTitleAr, request.JobTitleEn, request.ExpiresOn, today, out var identity) is { } invalid)
        {
            return invalid;
        }

        if (request.Roles is not { Count: > 0 } || request.Roles.Any(r => r is null)
            || request.Roles.Select(r => (r.RoleId, r.OrgUnitId)).Distinct().Count() != request.Roles.Count
            || request.Roles.Any(r => r.ValidFrom is { } from && r.ValidTo is { } to && to < from))
        {
            return AssignmentQueries.Invalid(nameof(request.Roles), "roles_required");
        }

        var access = await http.CheckUnitAsync(Permissions.Staff.Create, request.HomeOrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var home = access.Unit!;
        var (targets, problem) = await CheckRolesAsync(request.Roles, home.OrganizationId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var email = identity.Email!;
        if (await duplicates.FindStaffConflictAsync(home.OrganizationId, email, identity.Mobile, identity.NationalId, null, ct)
            is { } field)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Conflict",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.duplicate_person, ["field"] = field });
        }

        if (request.PhotoFileId is { } photo && !await db.Set<StoredFile>().AnyAsync(f => f.Id == photo, ct))
        {
            return AssignmentQueries.Invalid(nameof(request.PhotoFileId), "file_not_found");
        }

        var created = await creator.AddAsync(new StaffDraft(
            home, request.NameAr.Trim(), request.NameEn.Trim(), email, identity.Mobile, identity.NationalId,
            StaffValidators.Clean(request.JobTitleAr), StaffValidators.Clean(request.JobTitleEn), request.PhotoFileId,
            request.ExpiresOn, targets.Select(t => new StaffRoleDraft(t.Role, t.Unit, t.Input.ValidFrom, t.Input.ValidTo)).ToList()), ct);
        if (created.AssignmentExists)
        {
            return ApiProblems.Conflict(ProblemCodes.assignment_exists);
        }

        await db.SaveChangesAsync(ct);

        await creator.InviteAsync(created.User!, home, ct);
        var profile = created.Profile!;
        return await queries.GetAsync(profile.Id, ct, Routes.StaffMember(profile.Id));
    }

    private async Task<(List<(Role Role, OrgUnit Unit, StaffRoleInput Input)> Targets, IResult? Problem)> CheckRolesAsync(
        IReadOnlyList<StaffRoleInput> inputs, Guid organizationId, HttpContext http, CancellationToken ct)
    {
        var targets = new List<(Role, OrgUnit, StaffRoleInput)>();
        foreach (var input in inputs)
        {
            var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == input.RoleId && r.OrganizationId == organizationId, ct);
            if (role is null)
            {
                return (targets, ApiProblems.NotFound());
            }

            if (role.BaseKind != RoleBaseKind.Staff)
            {
                return (targets, ApiProblems.BadRequest(ProblemCodes.invalid_role_kind));
            }

            var access = await http.CheckUnitAsync(Permissions.Assignments.Create, input.OrgUnitId, ct);
            if (access.Problem is not null)
            {
                return (targets, access.Problem);
            }

            if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, role.Id, ct))
            {
                return (targets, Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                    extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.permission_escalation }));
            }

            var grant = await roleGuard.CheckAsync(role, access.Unit!, ct);
            if (!grant.Allowed)
            {
                return (targets, AccessProblem.From(grant, http));
            }

            targets.Add((role, access.Unit!, input));
        }

        return (targets, null);
    }
}
