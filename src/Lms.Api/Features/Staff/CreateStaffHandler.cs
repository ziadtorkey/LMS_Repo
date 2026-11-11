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
    LmsDbContext db, IClock clock, CallerPermissions caller, IPersonDuplicateFinder duplicates, IAccountEmails emails,
    ISettingsResolver settings, IRoleGrantGuard roleGuard, StaffQueries queries, ILogger<CreateStaffHandler> logger)
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

        var existing = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), ct);
        var user = existing ?? NewUser(request, email);
        foreach (var (role, unit, input) in targets)
        {
            if (existing is not null
                && await AssignmentQueries.OverlapsAsync(db, user.Id, role.Id, unit.Id, input.ValidFrom, input.ValidTo, null, ct))
            {
                return ApiProblems.Conflict(ProblemCodes.assignment_exists);
            }
        }

        if (existing is null)
        {
            db.Users.Add(user);
        }

        var profile = new StaffProfile(
            home, user.Id, request.NameAr.Trim(), request.NameEn.Trim(), email, identity.Mobile, identity.NationalId,
            StaffValidators.Clean(request.JobTitleAr), StaffValidators.Clean(request.JobTitleEn), request.PhotoFileId,
            request.ExpiresOn);
        db.Add(profile);
        db.RoleAssignments.AddRange(targets.Select(t => new RoleAssignment(t.Unit, user.Id, t.Role.Id, t.Input.ValidFrom, t.Input.ValidTo)));
        await db.SaveChangesAsync(ct);

        await InviteAsync(user, home, ct);
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

    private static User NewUser(CreateStaffRequest request, string email) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayNameAr = request.NameAr.Trim(),
        DisplayNameEn = request.NameEn.Trim(),
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        LockoutEnabled = true,
    };

    /// <summary>Invitation for a person who has no password yet; the profile and roles exist even if the mail fails.</summary>
    private async Task InviteAsync(User user, OrgUnit home, CancellationToken ct)
    {
        if (user.PasswordHash is not null)
        {
            return;
        }

        try
        {
            var culture = (await settings.ResolveAsync(SettingKeys.UiDefaultCulture, home.Path, ct)).Value;
            await emails.SendInvitationAsync(user, culture, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invitation email to user {UserId} could not be sent", user.Id);
        }
    }
}
