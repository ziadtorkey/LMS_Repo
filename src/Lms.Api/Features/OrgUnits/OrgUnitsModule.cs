using Lms.Api.Infrastructure;
using Lms.Domain.Organizations;

namespace Lms.Api.Features.OrgUnits;

/// <summary>The org tree: unit CRUD, search, status, delete rules.</summary>
public sealed class OrgUnitsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<OrgUnitQueries>();
        services.AddScoped<DeleteOrgUnitHandler>();
        services.AddScoped<AcademicFlagsHandler>();
        services.AddScoped<IOrgUnitUsageCheck, TransferTargetUsageCheck>();
        services.AddScoped<MoveOrgUnitHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapOrgUnitEndpoints();
}