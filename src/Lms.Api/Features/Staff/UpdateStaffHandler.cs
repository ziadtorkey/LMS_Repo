using Lms.Api.Features.Account;
using Lms.Api.Features.Assignments;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>
/// PUT /api/staff/{id} (profile and home unit; the e-mail is the login identity and never changes) and
/// POST /api/staff/{id}/resend-invitation.
/// </summary>
public sealed class UpdateStaffHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, IPersonDuplicateFinder duplicates, StaffQueries queries,
    IAccountEmails emails, ISettingsResolver settings, ILogger<UpdateStaffHandler> logger)
{
    public async Task<IResult> UpdateAsync(Guid id, UpdateStaffRequest? request, HttpContext http, CancellationToken ct)
    {
        if (request is null)
        {
            return AssignmentQueries.Invalid(nameof(request), "required");
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (StaffValidators.Validate(
                request.NameAr, request.NameEn, null, false, request.Mobile, request.NationalId,
                request.JobTitleAr, request.JobTitleEn, request.ExpiresOn, today, out var identity) is { } invalid)
        {
            return invalid;
        }

        var (profile, problem) = await StaffAccess.FindForUpdateAsync(db, context, http, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var moving = request.HomeOrgUnitId != profile!.OrgUnitId;
        Lms.Domain.Organizations.OrgUnit? newHome = null;
        if (moving)
        {
            var target = await http.CheckUnitAsync(Permissions.Staff.Update, request.HomeOrgUnitId, ct);
            if (target.Problem is not null)
            {
                return target.Problem;
            }

            newHome = target.Unit!;
        }

        if (await duplicates.FindStaffConflictAsync(profile.OrganizationId, profile.Email, identity.Mobile, identity.NationalId, profile.Id, ct)
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

        profile.Update(
            request.NameAr.Trim(), request.NameEn.Trim(), identity.Mobile, identity.NationalId,
            StaffValidators.Clean(request.JobTitleAr), StaffValidators.Clean(request.JobTitleEn), request.PhotoFileId, request.ExpiresOn);
        if (newHome is not null)
        {
            profile.MoveHome(newHome);

            // The history follows the person: the new home's admins see it, the old unit's admins no longer do.
            var history = await db.Set<PersonStatusChange>()
                .Where(c => c.Kind == PersonKind.Staff && c.ProfileId == profile.Id).ToListAsync(ct);
            foreach (var change in history)
            {
                change.MoveHome(newHome.Id, newHome.Path);
            }
        }

        await db.SaveChangesAsync(ct);
        return await queries.GetAsync(profile.Id, ct);
    }

    public async Task<IResult> ResendInvitationAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (profile, problem) = await StaffAccess.FindForUpdateAsync(db, context, http, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == profile!.UserId, ct);
        if (user is null)
        {
            return ApiProblems.NotFound();
        }

        if (user.PasswordHash is not null)
        {
            return ApiProblems.Conflict(ProblemCodes.already_activated);
        }

        var culture = (await settings.ResolveAsync(SettingKeys.UiDefaultCulture, profile!.OrgUnitPath, ct)).Value;
        try
        {
            await emails.SendInvitationAsync(user, culture, ct);
        }
        catch (Exception ex)
        {
            // Same as account creation: a mail server that is down must not turn the action into a server error.
            logger.LogWarning(ex, "Invitation email to user {UserId} could not be sent", user.Id);
        }

        return Results.NoContent();
    }
}
