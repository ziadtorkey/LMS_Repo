using Lms.Shared.Authorization;

namespace Lms.Api.Infrastructure;

public static class EndpointAuthorizationExtensions
{
    /// <summary>Requires the permission on a single endpoint or on every endpoint of a route group.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!Permissions.All.Contains(permission))
        {
            throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
        }

        return builder.AddEndpointFilter(new PermissionEndpointFilter(permission));
    }
}