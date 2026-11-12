using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Classes;

/// <summary>Classes: CRUD, bulk create, copy to the next year and close (FR-CLS-01, FR-CLS-02, FR-CLS-04).</summary>
public sealed class ClassesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ClassHandlers>();
        services.AddScoped<BulkClassHandlers>();
        services.AddScoped<ClassMapper>();
        services.AddScoped<ClassLookups>();
        services.AddScoped<ClassCourseWriter>();
        services.AddScoped<ClassCourseMapper>();
        services.AddScoped<ClassCourseHandlers>();
        services.AddScoped<MatrixQuery>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        ClassEndpoints.Map(app);
        ClassCourseEndpoints.Map(app);
    }
}
