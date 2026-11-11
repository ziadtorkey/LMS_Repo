using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Assignments;

/// <summary>Role assignments admin: who holds which role where and for how long (PRD §3.3, FR-SEC-06).</summary>
public sealed class AssignmentsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<CreateAssignmentHandler>();

    public void MapEndpoints(IEndpointRouteBuilder app) => AssignmentEndpoints.Map(app);
}
