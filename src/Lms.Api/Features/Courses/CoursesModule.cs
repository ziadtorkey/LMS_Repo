using Lms.Api.Features.Courses.Import;
using Lms.Api.Infrastructure;
using Lms.Infrastructure.Imports;

namespace Lms.Api.Features.Courses;

/// <summary>Courses: catalog CRUD, status and delete (M3 Task 6; discovered automatically).</summary>
public sealed class CoursesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddImportDefinition<CourseImportDefinition>();

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapCourseEndpoints();
}
