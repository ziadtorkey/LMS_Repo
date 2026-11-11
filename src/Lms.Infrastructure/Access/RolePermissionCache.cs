using Lms.Domain.Access;
using Microsoft.Extensions.Caching.Memory;

namespace Lms.Infrastructure.Access;

/// <summary>The cache key convention shared by the access evaluator (reads) and role admin (invalidation).</summary>
public sealed class RolePermissionCache(IMemoryCache cache) : IRolePermissionCache
{
    public static string Key(Guid roleId) => $"role-permissions:{roleId}";

    public void Invalidate(Guid roleId) => cache.Remove(Key(roleId));
}
