using System.Net.Mail;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Shared.Configuration;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Assignments;

/// <summary>
/// POST /api/assignments: assign a role at a unit by email (D21). An unknown email creates a bare user and sends the
/// invitation; there is no user lookup, so one client cannot browse another client's users.
/// </summary>
public sealed class CreateAssignmentHandler(
    LmsDbContext db, IClock clock, CallerPermissions caller, IAccountEmails emails, ISettingsResolver settings,
    AssignmentNotifier notifier, ILogger<CreateAssignmentHandler> logger)
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

        if (!await AssignmentQueries.CallerMayManageRoleAsync(db, caller, role.Id, ct))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.permission_escalation });
        }

        var normalized = email.ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        var isNew = user is null;
        if (user is null)
        {
            var nameAr = request.DisplayNameAr?.Trim();
            var nameEn = request.DisplayNameEn?.Trim();
            if (string.IsNullOrEmpty(nameAr) || string.IsNullOrEmpty(nameEn) || nameAr.Length > 200 || nameEn.Length > 200)
            {
                return AssignmentQueries.Invalid(nameof(request.DisplayNameEn), "display_names_required");
            }

            user = new User
            {
                UserName = email,
                NormalizedUserName = normalized,
                Email = email,
                NormalizedEmail = normalized,
                DisplayNameAr = nameAr,
                DisplayNameEn = nameEn,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                LockoutEnabled = true,
            };
            db.Users.Add(user);
        }
        else if (await AssignmentQueries.OverlapsAsync(
                     db, user.Id, role.Id, unit.Id, request.ValidFrom, request.ValidTo, null, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.assignment_exists);
        }

        // One SaveChanges: a new user and their assignment are created together or not at all.
        var assignment = new RoleAssignment(unit, user.Id, role.Id, request.ValidFrom, request.ValidTo);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        if (isNew)
        {
            await InviteAsync(user, unit, ct);
        }

        await notifier.RoleAssignedAsync(assignment, role, unit, ct);

        return await AssignmentQueries.GetAsync(db, clock, assignment.Id, ct) is { } dto
            ? Results.Created(Routes.Assignment(assignment.Id), dto)
            : ApiProblems.NotFound();
    }

    private async Task InviteAsync(User user, OrgUnit unit, CancellationToken ct)
    {
        try
        {
            // The invitee has no language yet: use the default culture of the unit they are invited to, not the admin's.
            var culture = (await settings.ResolveAsync(SettingKeys.UiDefaultCulture, unit.Path, ct)).Value;
            await emails.SendInvitationAsync(user, culture, ct);
        }
        catch (Exception ex)
        {
            // The account and assignment exist; a mail outage must not undo them. An admin can send a reset link later.
            logger.LogWarning(ex, "Invitation email to user {UserId} could not be sent", user.Id);
        }
    }
}
