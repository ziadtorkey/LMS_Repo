using Lms.Api.Features.Enrollment.Import;
using Lms.Api.Infrastructure;
using Lms.Infrastructure.Imports;

namespace Lms.Api.Features.Enrollment;

/// <summary>Class placement and course enrollment endpoints (M3 Task 24); the engine itself is registered by <c>AddEnrollment()</c>.</summary>
public sealed class EnrollmentModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<EnrollmentRelationship>();
        services.AddScoped<EnrollmentQueries>();
        services.AddImportDefinition<ClassStudentsImportDefinition>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ClassStudentEndpoints.Map(app);
        EnrollmentEndpoints.Map(app);
    }
}
