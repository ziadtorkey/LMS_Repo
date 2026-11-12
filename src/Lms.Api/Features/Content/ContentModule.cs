using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Content;

/// <summary>Teaching content: assets today; lesson modules, completion and progress follow in the M4a tasks.</summary>
public sealed class ContentModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => AssetEndpoints.Map(app);
}
