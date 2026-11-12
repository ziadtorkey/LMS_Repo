using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Library;

/// <summary>The digital library (M4a Task 12, FR-LIB-01).</summary>
public sealed class LibraryModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LibraryQueries>();
        services.AddScoped<LibraryMapper>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => LibraryEndpoints.Map(app);
}
