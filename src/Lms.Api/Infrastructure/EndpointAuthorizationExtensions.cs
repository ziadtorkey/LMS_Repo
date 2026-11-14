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

        builder.Add(endpoint => endpoint.Metadata.Add(new RequiresPermissionMetadata(permission)));
        return builder.AddEndpointFilter(new PermissionEndpointFilter(permission));
    }

    /// <summary>
    /// Requires any one of the permissions (a route several roles reach with different permissions, such as Attendance.Take, Edit or View). The first one is the
    /// metadata the coverage tests read; the handler still checks the relationship or unit scope of the concrete row.
    /// </summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        if (permissions.Length == 0 || permissions.Any(p => !Permissions.All.Contains(p)))
        {
            throw new ArgumentException("Unknown or missing permission.", nameof(permissions));
        }

        builder.Add(endpoint => endpoint.Metadata.Add(new RequiresPermissionMetadata(permissions[0])));
        return builder.AddEndpointFilter(new AnyPermissionEndpointFilter(permissions));
    }
}