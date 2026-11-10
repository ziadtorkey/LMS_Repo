using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Access;

/// <summary>
/// Brings built-in roles of clients provisioned under an older defaults version up to date. Add-only and once
/// per role: only defaults introduced after the role's version are added (so a permission an admin removed from
/// an older default is never re-added), nothing is ever removed, and custom roles are not touched.
/// </summary>
public sealed class BuiltInRoleReconciler(LmsDbContext db)
{
    public async Task<int> ReconcileAsync(CancellationToken cancellationToken)
    {
        var stale = await db.Roles
            .Where(r => r.BuiltInKey != null && r.DefaultsVersion < BuiltInRoleDefaults.CurrentVersion)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return 0;
        }

        var roleIds = stale.Select(r => r.Id).ToList();
        var existing = (await db.RolePermissions.Where(p => roleIds.Contains(p.RoleId))
                .Select(p => new { p.RoleId, p.Permission }).ToListAsync(cancellationToken))
            .Select(p => (p.RoleId, p.Permission)).ToHashSet();

        foreach (var role in stale)
        {
            var definition = BuiltInRoles.All.FirstOrDefault(d => d.Key == role.BuiltInKey);
            if (definition is not null)
            {
                // Versions 0 → 1: the defaults that did not exist in M0.
                foreach (var permission in definition.DefaultPermissions.Where(p => !BuiltInRoleDefaults.M0Baseline.Contains(p)))
                {
                    if (!existing.Contains((role.Id, permission)))
                    {
                        db.RolePermissions.Add(new Domain.Access.RolePermission(role.OrganizationId, role.Id, permission));
                    }
                }
            }

            role.MarkDefaultsApplied(BuiltInRoleDefaults.CurrentVersion);
        }

        await db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }
}