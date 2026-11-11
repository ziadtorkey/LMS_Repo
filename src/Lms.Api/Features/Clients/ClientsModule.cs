using Lms.Api.Infrastructure;

namespace Lms.Api.Features.Clients;

/// <summary>Client administration for the Platform Admin: list, create with the first Org Admin, rename, suspend.</summary>
public sealed class ClientsModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<CreateClientHandler>();

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapClientEndpoints();
}