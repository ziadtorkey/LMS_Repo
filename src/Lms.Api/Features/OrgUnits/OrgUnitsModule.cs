using Lms.Api.Infrastructure;

namespace Lms.Api.Features.OrgUnits;

/// <summary>The org tree: unit CRUD, search, status, delete rules.</summary>
public sealed class OrgUnitsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<OrgUnitQueries>();
        services.AddScoped<DeleteOrgUnitHandler>();
        services.AddScoped<MoveOrgUnitHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapOrgUnitEndpoints();
}