using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;

namespace Lms.Infrastructure.Access;

public sealed class RoleGrantGuard(IPolicyEvaluator policies) : IRoleGrantGuard
{
    public async Task<AccessDecision> CheckAsync(Role role, OrgUnit unit, CancellationToken cancellationToken)
    {
        if (role.OrganizationId != unit.OrganizationId)
        {
            return AccessDecision.Deny(AccessDenialReason.OutOfScope);
        }

        var blocking = await policies.FindBlockingUnitForRoleAsync(role, unit.Path, cancellationToken);
        return blocking is { } id ? AccessDecision.Deny(AccessDenialReason.BlockedByPolicy, id) : AccessDecision.Allow;
    }
}
