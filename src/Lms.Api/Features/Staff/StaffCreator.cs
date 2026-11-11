using Lms.Api.Features.Assignments;
using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>A role the new staff member gets: the resolved role, the unit and the validity window.</summary>
public sealed record StaffRoleDraft(Role Role, OrgUnit Unit, DateOnly? ValidFrom, DateOnly? ValidTo);

/// <summary>Everything needed to create one staff member (already validated and normalized by the caller).</summary>
public sealed record StaffDraft(
    OrgUnit Home, string NameAr, string NameEn, string Email, string? Mobile, string? NationalId, string? JobTitleAr,
    string? JobTitleEn, Guid? PhotoFileId, DateOnly? ExpiresOn, IReadOnlyList<StaffRoleDraft> Roles);

/// <summary>The outcome of <see cref="StaffCreator.Add"/>; nothing is saved yet.</summary>
public sealed record StaffCreation(User? User, StaffProfile? Profile, bool AssignmentExists);

/// <summary>
/// The domain steps of creating a staff member, shared by <c>POST /api/staff</c> and the staff import (M2 Task 17): reuse the
/// login account when the e-mail belongs to a person of another client, else create it; the profile; the role assignments.
/// The caller saves (the import inside its all-or-nothing transaction) and invites after the commit.
/// </summary>
public sealed class StaffCreator(
    LmsDbContext db, IAccountEmails emails, ISettingsResolver settings, ILogger<StaffCreator> logger)
{
    public async Task<StaffCreation> AddAsync(StaffDraft draft, CancellationToken ct)
    {
        var normalized = draft.Email.ToUpperInvariant();
        var existing = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        var user = existing ?? NewUser(draft);
        if (existing is not null)
        {
            foreach (var role in draft.Roles)
            {
                if (await AssignmentQueries.OverlapsAsync(db, user.Id, role.Role.Id, role.Unit.Id, role.ValidFrom, role.ValidTo, null, ct))
                {
                    return new StaffCreation(null, null, AssignmentExists: true);
                }
            }
        }
        else
        {
            db.Users.Add(user);
        }

        var profile = new StaffProfile(
            draft.Home, user.Id, draft.NameAr, draft.NameEn, draft.Email, draft.Mobile, draft.NationalId,
            draft.JobTitleAr, draft.JobTitleEn, draft.PhotoFileId, draft.ExpiresOn);
        db.Add(profile);
        db.RoleAssignments.AddRange(draft.Roles.Select(r => new RoleAssignment(r.Unit, user.Id, r.Role.Id, r.ValidFrom, r.ValidTo)));
        return new StaffCreation(user, profile, AssignmentExists: false);
    }

    /// <summary>Invitation for a person who has no password yet; the profile and roles exist even if the mail fails.</summary>
    public async Task InviteAsync(User user, OrgUnit home, CancellationToken ct)
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

    private static User NewUser(StaffDraft draft) => new()
    {
        UserName = draft.Email,
        NormalizedUserName = draft.Email.ToUpperInvariant(),
        Email = draft.Email,
        NormalizedEmail = draft.Email.ToUpperInvariant(),
        DisplayNameAr = draft.NameAr,
        DisplayNameEn = draft.NameEn,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        ConcurrencyStamp = Guid.NewGuid().ToString("N"),
        LockoutEnabled = true,
    };
}
