using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People.Guardians;

/// <inheritdoc cref="IGuardianAccountSync" />
public sealed class GuardianAccountSync(
    LmsDbContext db, ISettingsResolver settings, IPersonAccounts accounts, IInvitationOutbox outbox)
    : IGuardianAccountSync, IGuardianLinkObserver
{
    public Task OnLinksChangedAsync(GuardianProfile guardian, CancellationToken cancellationToken) =>
        SyncAsync(guardian, resendInvitation: false, cancellationToken);

    public async Task<GuardianPortalPlan> PlanAsync(GuardianProfile guardian, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the children of a guardian can be in schools the caller cannot see (siblings); the guardian's
        // contexts must cover all of them, or a sync would remove the others. Client matched and deleted rows excluded by hand.
        var unitIds = await (
                from link in db.Set<StudentGuardian>().IgnoreQueryFilters()
                join student in db.Set<StudentProfile>().IgnoreQueryFilters() on link.StudentId equals student.Id
                where link.OrganizationId == guardian.OrganizationId && link.GuardianId == guardian.Id
                      && !link.IsDeleted && !student.IsDeleted && student.OrganizationId == guardian.OrganizationId
                select student.OrgUnitId)
            .Distinct()
            .ToListAsync(ct);

        // ISOLATION-BYPASS: the home units of those children, same reason; client matched, deleted units excluded.
        var units = await db.Set<OrgUnit>().IgnoreQueryFilters()
            .Where(u => unitIds.Contains(u.Id) && u.OrganizationId == guardian.OrganizationId && !u.IsDeleted)
            .OrderBy(u => u.Path)
            .ToListAsync(ct);

        var enabled = new List<OrgUnit>();
        foreach (var unit in units)
        {
            if ((await settings.ResolveAsync(FeatureFlags.GuardianPortal, unit.Path, ct)).Value)
            {
                enabled.Add(unit);
            }
        }

        var activated = guardian.UserId is { } userId
            && await db.Users.AnyAsync(u => u.Id == userId && u.PasswordHash != null, ct);
        return new GuardianPortalPlan(units, enabled, !string.IsNullOrWhiteSpace(guardian.Email), activated);
    }

    public async Task SyncAsync(GuardianProfile guardian, bool resendInvitation, CancellationToken ct)
    {
        var plan = await PlanAsync(guardian, ct);
        var role = await db.Set<Role>().FirstOrDefaultAsync(
            r => r.OrganizationId == guardian.OrganizationId && r.BuiltInKey == BuiltInRoles.Guardian, ct);
        if (role is null)
        {
            return;
        }

        if (plan.EnabledUnits.Count == 0 || !plan.HasEmail)
        {
            // Portal off everywhere, or nowhere to send the credentials: no contexts. The account (if any) is kept.
            if (guardian.UserId is { } existing)
            {
                await SyncAssignmentsAsync(guardian.OrganizationId, role, existing, [], ct);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        var email = PersonNormalizer.Email(guardian.Email)!;
        var (user, created) = await accounts.EnsureUserAsync(email, email, guardian.NameAr, guardian.NameEn, ct);
        if (guardian.UserId != user.Id)
        {
            guardian.LinkUser(user.Id);
        }

        await SyncAssignmentsAsync(guardian.OrganizationId, role, user.Id, plan.EnabledUnits, ct);
        await db.SaveChangesAsync(ct);

        if (created || resendInvitation)
        {
            // The invitee has no language yet: the default culture of the first unit they get a context in.
            var culture = (await settings.ResolveAsync(SettingKeys.UiDefaultCulture, plan.EnabledUnits[0].Path, ct)).Value;
            outbox.Enqueue(user.Id, culture);
            await outbox.FlushAsync(ct); // the links are saved: nothing to roll back; users with a password are skipped
        }
    }

    /// <summary>One Guardian assignment per unit: missing ones added, the others soft-deleted. Sees the whole client.</summary>
    private async Task SyncAssignmentsAsync(Guid organizationId, Role role, Guid userId, IReadOnlyList<OrgUnit> units, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the guardian's existing assignments sit at the units of children outside the caller's scope;
        // without them the sync would add duplicates and could not remove stale ones. Client matched, deleted rows excluded.
        var live = await db.Set<RoleAssignment>().IgnoreQueryFilters()
            .Where(a => a.OrganizationId == organizationId && a.RoleId == role.Id && a.UserId == userId && !a.IsDeleted)
            .ToListAsync(ct);
        var wanted = units.Select(u => u.Id).ToHashSet();
        foreach (var extra in live.Where(a => !wanted.Contains(a.OrgUnitId)))
        {
            db.Set<RoleAssignment>().Remove(extra);
        }

        var have = live.Select(a => a.OrgUnitId).ToHashSet();
        foreach (var unit in units.Where(u => !have.Contains(u.Id)))
        {
            db.Set<RoleAssignment>().Add(new RoleAssignment(unit, userId, role.Id));
        }
    }
}
