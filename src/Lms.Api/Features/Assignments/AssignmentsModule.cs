namespace Lms.Api.Features.Assignments;

/// <summary>Role assignments admin: who holds which role where and for how long (PRD §3.3, FR-SEC-06).</summary>
public static class AssignmentsModule
{
    public static IServiceCollection AddAssignments(this IServiceCollection services)
    {
        services.AddScoped<CreateAssignmentHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        AssignmentEndpoints.Map(app);
        return app;
    }
}
