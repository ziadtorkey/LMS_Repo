using Lms.Domain.Organizations;

namespace Lms.Domain.Access;

/// <summary>
/// The one rule for "may this role be granted at this unit": the role belongs to the unit's client and no policy
/// above the unit blocks it (D28). Every path that creates an assignment (assignments API, staff create, staff share,
/// staff import) goes through it. Privilege escalation stays in <c>RolePermissionRules</c>.
/// </summary>
public interface IRoleGrantGuard
{
    Task<AccessDecision> CheckAsync(Role role, OrgUnit unit, CancellationToken cancellationToken);
}
