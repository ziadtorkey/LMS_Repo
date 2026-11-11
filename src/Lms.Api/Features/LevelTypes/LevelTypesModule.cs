using Lms.Api.Infrastructure;

namespace Lms.Api.Features.LevelTypes;

/// <summary>Level types of a client (FR-ORG-02): the kinds of node its org tree is built from.</summary>
public sealed class LevelTypesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapLevelTypeEndpoints();
}
