using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Gradebook;

/// <summary>The staff gradebook: grid, configuration, categories, items and entries (M5 Task 8). Services are registered by the infrastructure.</summary>
public sealed class GradebookModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGradebookEndpoints();
        app.MapGradebookConfigEndpoints();
        app.MapGradeEntryEndpoints();
    }
}
