using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Enrollment;

/// <summary>Class placement and course enrollment endpoints (M3 Task 24); the engine itself is registered by <c>AddEnrollment()</c>.</summary>
public sealed class EnrollmentModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EnrollmentRelationship>();
        services.AddScoped<EnrollmentQueries>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ClassStudentEndpoints.Map(app);
        EnrollmentEndpoints.Map(app);
    }
}
