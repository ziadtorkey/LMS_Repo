using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
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

    /// <summary>Permissions of the role behind an assignment, cached per assignment for 60 seconds.</summary>
    private async Task<HashSet<string>> GetRolePermissionsAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrCreateAsync($"role-permissions:{assignmentId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PermissionCacheTtl;

            // The assignment sits at the caller's own scope unit, so the normal filters see it; a deleted role or
            // assignment yields an empty set. RolePermissions are filtered by the caller's client.
            var roleId = await db.RoleAssignments
                .Where(a => a.Id == assignmentId)
                .Select(a => (Guid?)a.RoleId)
                .FirstOrDefaultAsync(cancellationToken);
            if (roleId is null)
            {
                return new HashSet<string>();
            }

            var roleIsLive = await db.Roles.AnyAsync(r => r.Id == roleId, cancellationToken);
            if (!roleIsLive)
            {
                return new HashSet<string>();
            }

            return (await db.RolePermissions
                .Where(p => p.RoleId == roleId)
                .Select(p => p.Permission)
                .ToListAsync(cancellationToken)).ToHashSet();
        });

        return cached!;
    }
}