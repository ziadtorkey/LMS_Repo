using Lms.Domain.Access;

namespace Lms.Infrastructure.Configuration;

public interface IPolicyEvaluator
{
    /// <summary>The highest ancestor-or-self unit holding a policy that blocks the permission, or null.</summary>
    Task<Guid?> FindBlockingUnitAsync(string permission, string orgUnitPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// The highest ancestor-or-self unit holding a policy that blocks assigning the role, or null. A policy blocks a
    /// built-in role by key (<c>PolicyCatalog.BlockedRoles</c>); <c>CreateAdmins</c> also blocks a custom role holding
    /// an admin-marker permission.
    /// </summary>
    Task<Guid?> FindBlockingUnitForRoleAsync(Role role, string orgUnitPath, CancellationToken cancellationToken = default);
}
