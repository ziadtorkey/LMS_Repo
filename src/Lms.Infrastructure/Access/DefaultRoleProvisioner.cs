using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Access;

public sealed class DefaultRoleProvisioner(LmsDbContext db) : IDefaultRoleProvisioner
{
    public async Task ProvisionAsync(Guid organizationId, OrganizationType type, CancellationToken cancellationToken)
    {
        var existing = await db.Roles
            .Where(r => r.OrganizationId == organizationId && r.BuiltInKey != null)
            .Select(r => r.BuiltInKey!)
            .ToListAsync(cancellationToken);

        foreach (var definition in BuiltInRoles.All.Where(d => !existing.Contains(d.Key)))
        {
            var label = type == OrganizationType.University ? definition.UniversityLabel : definition.SchoolGroupLabel;
            var role = new Role(
                organizationId, definition.Key, label.NameAr, label.NameEn,
                Enum.Parse<RoleBaseKind>(definition.BaseKind));
            role.MarkDefaultsApplied(BuiltInRoleDefaults.CurrentVersion);
            db.Roles.Add(role);
            db.RolePermissions.AddRange(
                definition.DefaultPermissions.Select(p => new RolePermission(organizationId, role.Id, p)));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}