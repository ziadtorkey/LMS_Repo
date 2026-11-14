using Lms.Api.Infrastructure;

namespace Lms.Api.Features.GradingScales;

/// <summary>Grading scales, bands, weight templates and the Saudi presets of an org unit (M5 Task 4). Services are registered by the infrastructure.</summary>
public sealed class GradingScalesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGradingScaleEndpoints();
        app.MapWeightTemplateEndpoints();
    }
}
