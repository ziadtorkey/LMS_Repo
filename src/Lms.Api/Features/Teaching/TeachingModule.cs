using Lms.Api.Infrastructure;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Teaching;

namespace Lms.Api.Features.Teaching;

/// <summary>Teacher eligibility and class-course teacher assignment with history (FR-TCH-01..03).</summary>
public sealed class TeachingModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TeachingLookups>();
        services.AddScoped<EligibilityHandlers>();
        services.AddScoped<TeacherAssignmentHandlers>();
        services.AddScoped<MyClassCoursesQuery>();
        services.AddScoped<ITeacherScheduleConflicts, NoScheduleConflicts>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => TeachingEndpoints.Map(app);
}
