using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Roles;

/// <summary>Roles admin: built-in permission sets and labels, custom roles (PRD FR-SEC-06).</summary>
public sealed class RolesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => RoleEndpoints.Map(app);
}
