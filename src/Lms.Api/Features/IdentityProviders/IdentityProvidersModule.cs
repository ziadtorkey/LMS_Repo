using Lms.Api.Infrastructure;
using Lms.Infrastructure.Identity.External;

namespace Lms.Api.Features.IdentityProviders;

/// <summary>Per-client external sign-in providers (FR-SEC-08): configuration API; the schemes live in the infrastructure registry.</summary>
public sealed class IdentityProvidersModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient(IdentityProviderEndpoints.DiscoveryClient, c => c.Timeout = TimeSpan.FromSeconds(5))
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<OutboundGuard>().CreateHandler());

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapIdentityProviderEndpoints();
}
