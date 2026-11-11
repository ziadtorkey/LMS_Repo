using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.People;

/// <summary>
/// M1 created "bare" users by email when assigning a role. Every user with a live staff-kind assignment in a client and
/// no live staff profile there gets one: names and email from the user, home unit = the unit of their highest (shortest
/// path) such assignment, status taken from the M1 account status (Withdrawn counts as Deactivated). Idempotent; safe to run at every start.
/// </summary>
public sealed class StaffBackfill(LmsDbContext db, ILogger<StaffBackfill> logger)
{
    /// <summary>Runs with a platform context (every client). Returns the number of profiles created.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // ISOLATION-BYPASS: startup maintenance over every client. Deleted rows are excluded and the client is matched
        // explicitly on every join; every profile written carries its client from the assignment's unit.
        var assignments = await (
                from a in db.Set<RoleAssignment>().IgnoreQueryFilters()
                join r in db.Set<Role>().IgnoreQueryFilters() on a.RoleId equals r.Id
                where !a.IsDeleted && !r.IsDeleted && r.OrganizationId == a.OrganizationId && r.BaseKind == RoleBaseKind.Staff
                select new { a.UserId, a.OrganizationId, a.OrgUnitId, a.OrgUnitPath })
            .ToListAsync(cancellationToken);
        if (assignments.Count == 0)
        {
            return 0;
        }

        // ISOLATION-BYPASS: same as above, to find who already has a live profile in which client.
        var existing = (await db.Set<StaffProfile>().IgnoreQueryFilters()
                .Where(p => !p.IsDeleted).Select(p => new { p.UserId, p.OrganizationId }).ToListAsync(cancellationToken))
            .Select(p => (p.UserId, p.OrganizationId)).ToHashSet();

        var created = 0;
        foreach (var group in assignments.GroupBy(a => (a.UserId, a.OrganizationId)).Where(g => !existing.Contains(g.Key)))
        {
            var (userId, organizationId) = group.Key;
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            var email = PersonNormalizer.Email(user?.Email);
            if (user is null || email is null)
            {
                logger.LogWarning("Staff backfill skipped user {UserId}: no user row or no email", userId);
                continue;
            }

            var home = await HomeUnitAsync(organizationId, group.OrderBy(a => a.OrgUnitPath.Length).Select(a => a.OrgUnitId), cancellationToken);
            if (home is null)
            {
                continue; // every unit of their assignments is deleted
            }

            var nameEn = string.IsNullOrWhiteSpace(user.DisplayNameEn) ? email : user.DisplayNameEn;
            var nameAr = string.IsNullOrWhiteSpace(user.DisplayNameAr) ? nameEn : user.DisplayNameAr;
            var profile = new StaffProfile(home, userId, nameAr, nameEn, email, null, null, null, null, null, null);
            // The M1 account status carries over: a deactivated or expired user must not become active through the backfill.
            profile.SetStatus(user.Status switch
            {
                UserStatus.Expired => PersonStatus.Expired,
                UserStatus.Deactivated or UserStatus.Withdrawn => PersonStatus.Deactivated,
                _ => PersonStatus.Active,
            });
            db.Set<StaffProfile>().Add(profile);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Staff backfill created {Count} staff profiles for existing users.", created);
        }

        return created;
    }

    private async Task<OrgUnit?> HomeUnitAsync(Guid organizationId, IEnumerable<Guid> unitIdsByDepth, CancellationToken ct)
    {
        foreach (var unitId in unitIdsByDepth)
        {
            // ISOLATION-BYPASS: see above; the client is matched and deleted units excluded by hand.
            var unit = await db.Set<OrgUnit>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Id == unitId && u.OrganizationId == organizationId && !u.IsDeleted, ct);
            if (unit is not null)
            {
                return unit;
            }
        }

        return null;
    }
}
