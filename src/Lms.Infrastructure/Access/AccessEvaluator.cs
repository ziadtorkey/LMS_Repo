using System.Collections.Frozen;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Lms.Infrastructure.Access;

public sealed class AccessEvaluator(
    LmsDbContext db, ICurrentContext context, IPolicyEvaluator policies, IMemoryCache cache) : IAccessEvaluator
{
    private static readonly TimeSpan PermissionCacheTtl = TimeSpan.FromSeconds(60);

    public async Task<AccessDecision> EvaluateAsync(
        string permission, string? resourceOrgUnitPath, CancellationToken cancellationToken)
    {
        if (context.UserId is null)
        {
            return AccessDecision.Deny(AccessDenialReason.NotAuthenticated);
        }

        var path = resourceOrgUnitPath ?? context.ScopePath;

        // Defence in depth: platform-only permissions belong to the Platform Admin, whatever a role row says.
        if (!context.IsPlatformAdmin && Permissions.PlatformOnly.Contains(permission))
        {
            return AccessDecision.Deny(AccessDenialReason.MissingPermission);
        }

        // Platform admins skip the role and scope checks (never the policy check).
        if (!context.IsPlatformAdmin)
        {
            if (context.AssignmentId is null || context.ScopePath is null
                || !(await GetRolePermissionsAsync(context.AssignmentId.Value, cancellationToken)).Contains(permission))
            {
                return AccessDecision.Deny(AccessDenialReason.MissingPermission);
            }

            if (path is null || !OrgUnitPath.IsWithin(path, context.ScopePath))
            {
                return AccessDecision.Deny(AccessDenialReason.OutOfScope);
            }
        }

        if (path is not null)
        {
            var blockingUnit = await policies.FindBlockingUnitAsync(permission, path, cancellationToken);
            if (blockingUnit is not null)
            {
                return AccessDecision.Deny(AccessDenialReason.BlockedByPolicy, blockingUnit);
            }
        }

        return AccessDecision.Allow;
    }

    /// <summary>
    /// Permissions of the role behind an assignment. The assignment-to-role lookup (and the role's liveness) is read on
    /// every call; only the role's permission set is cached, per role, until the role is saved
    /// (<see cref="IRolePermissionCache.Invalidate"/>) or 60 seconds pass.
    /// </summary>
    private async Task<IReadOnlySet<string>> GetRolePermissionsAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        // The assignment sits at the caller's own scope unit, so the normal filters see it; a deleted role or
        // assignment yields an empty set.
        var roleId = await db.RoleAssignments
            .Where(a => a.Id == assignmentId && db.Roles.Any(r => r.Id == a.RoleId))
            .Select(a => (Guid?)a.RoleId)
            .FirstOrDefaultAsync(cancellationToken);
        if (roleId is null)
        {
            return FrozenSet<string>.Empty;
        }

        var cached = await cache.GetOrCreateAsync(RolePermissionCache.Key(roleId.Value), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PermissionCacheTtl;
            return (await db.RolePermissions
                .Where(p => p.RoleId == roleId)
                .Select(p => p.Permission)
                .ToListAsync(cancellationToken)).ToFrozenSet(StringComparer.Ordinal);
        });

        return cached!;
    }
}
