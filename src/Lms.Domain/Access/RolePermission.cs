using Lms.Domain.Common;

namespace Lms.Domain.Access;

/// <summary>One permission granted by a role. Hard-deleted when a role's permission set is edited.</summary>
public sealed class RolePermission : Entity, ITenantOwned
{
    private RolePermission()
    {
        Permission = string.Empty;
    }

    public RolePermission(Guid organizationId, Guid roleId, string permission)
    {
        OrganizationId = organizationId;
        RoleId = roleId;
        Permission = permission;
    }

    public Guid OrganizationId { get; private set; }
    public Guid RoleId { get; private set; }
    public string Permission { get; private set; }
}