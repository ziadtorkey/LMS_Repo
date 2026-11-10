using Lms.Domain.Organizations;

namespace Lms.Domain.Access;

public interface IDefaultRoleProvisioner
{
    /// <summary>Creates the seven built-in roles (with default permissions) for a client; idempotent.</summary>
    Task ProvisionAsync(Guid organizationId, OrganizationType type, CancellationToken cancellationToken);
}