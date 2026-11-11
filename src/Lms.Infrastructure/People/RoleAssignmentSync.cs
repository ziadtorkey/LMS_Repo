using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.People;

public sealed class RoleAssignmentSync(LmsDbContext db) : IRoleAssignmentSync
{
    public async Task SyncBuiltInAsync(
        Guid userId, Guid organizationId, string builtInRoleKey, IReadOnlyCollection<OrgUnit> units, CancellationToken cancellationToken)
    {
        if (units.Any(u => u.OrganizationId != organizationId))
        {
            throw new ArgumentException("Every unit must belong to the client.", nameof(units));
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.OrganizationId == organizationId && r.BuiltInKey == builtInRoleKey, cancellationToken)
            ?? throw new InvalidOperationException($"Built-in role {builtInRoleKey} does not exist in the client.");

        var live = await db.RoleAssignments
            .Where(a => a.UserId == userId && a.RoleId == role.Id && a.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        var wanted = units.Select(u => u.Id).ToHashSet();

        foreach (var extra in live.Where(a => !wanted.Contains(a.OrgUnitId)))
        {
            db.RoleAssignments.Remove(extra); // soft delete
        }

        var have = live.Select(a => a.OrgUnitId).ToHashSet();
        foreach (var unit in units.Where(u => !have.Contains(u.Id)))
        {
            db.RoleAssignments.Add(new RoleAssignment(unit, userId, role.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
