using Lms.Domain.Abstractions;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Excuses;

/// <summary>A staff user an excuse can be escalated to, with the built-in role that makes them eligible.</summary>
public sealed record EscalationCandidate(Guid UserId, string RoleKey, string NameAr, string NameEn);

/// <summary>
/// Who an excuse can be escalated to (plan L14): active users with a currently valid assignment, at or above the student's unit in the same client, of a
/// built-in role that holds <c>Excuses.Review</c> or <c>Excuses.Escalate</c> (Org Admin, Manager, Counselor). Custom roles are not offered yet.
/// </summary>
public sealed class ExcuseEscalationTargets(LmsDbContext db, IClock clock)
{
    private static readonly string[] EligibleRoleKeys =
        BuiltInRoles.All.Where(r => r.DefaultPermissions.Contains(Permissions.Excuses.Review) || r.DefaultPermissions.Contains(Permissions.Excuses.Escalate))
            .Select(r => r.Key).ToArray();

    public async Task<IReadOnlyList<EscalationCandidate>> ForAsync(Guid organizationId, string studentUnitPath, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var ancestors = OrgUnitPath.AncestorsAndSelf(studentUnitPath);

        // ISOLATION-BYPASS: the reviewer who escalates holds a narrower scope than the managers above the student's school; assignments are read at the
        // student's unit and its ancestors only, client matched, deleted rows and inactive users excluded; only ids, names and the role key leave.
        var rows = await (
                from assignment in db.RoleAssignments.IgnoreQueryFilters()
                join role in db.Roles.IgnoreQueryFilters() on assignment.RoleId equals role.Id
                join user in db.Users on assignment.UserId equals user.Id
                where !assignment.IsDeleted && assignment.OrganizationId == organizationId && ancestors.Contains(assignment.OrgUnitPath)
                      && (assignment.ValidFrom == null || assignment.ValidFrom <= today)
                      && (assignment.ValidTo == null || assignment.ValidTo >= today)
                      && !role.IsDeleted && role.OrganizationId == organizationId && role.BuiltInKey != null && EligibleRoleKeys.Contains(role.BuiltInKey)
                      && user.Status == UserStatus.Active
                select new { user.Id, RoleKey = role.BuiltInKey!, user.DisplayNameAr, user.DisplayNameEn })
            .ToListAsync(ct);

        return rows.GroupBy(r => r.Id)
            .Select(g => g.OrderBy(r => r.RoleKey == BuiltInRoles.Manager ? 0 : 1).First())
            .Select(r => new EscalationCandidate(r.Id, r.RoleKey, r.DisplayNameAr, r.DisplayNameEn))
            .ToList();
    }
}
