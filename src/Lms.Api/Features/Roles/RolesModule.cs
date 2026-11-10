namespace Lms.Api.Features.Roles;

/// <summary>Roles admin: built-in permission sets and labels, custom roles (PRD FR-SEC-06).</summary>
public static class RolesModule
{
    public static IServiceCollection AddRoles(this IServiceCollection services) => services;

    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder app)
    {
        RoleEndpoints.Map(app);
        return app;
    }
}
