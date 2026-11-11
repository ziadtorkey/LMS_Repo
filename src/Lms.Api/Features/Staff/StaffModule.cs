using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Staff;

/// <summary>Staff administration: create, list and profile (FR-STF-01..03); edit and status follow in Task 11.</summary>
public sealed class StaffModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CreateStaffHandler>();
        services.AddScoped<StaffQueries>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => StaffEndpoints.Map(app);
}
