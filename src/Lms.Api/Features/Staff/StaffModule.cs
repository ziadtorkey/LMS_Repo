using Lms.Api.Infrastructure;
using Lms.Infrastructure.Imports;

namespace Lms.Api.Features.Staff;

/// <summary>Staff administration: create, list and profile (FR-STF-01..03); edit and status follow in Task 11.</summary>
public sealed class StaffModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CreateStaffHandler>();
        services.AddScoped<StaffCreator>();
        services.AddImportDefinition<Import.StaffImportDefinition>();
        services.AddScoped<StaffQueries>();
        services.AddScoped<ShareStaffHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => StaffEndpoints.Map(app);
}
