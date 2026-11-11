using Lms.Api.Infrastructure;
using Lms.Infrastructure.Configuration;

namespace Lms.Api.Features.Policies;

/// <summary>The policies control panel (FR-ORG-10): switch policies on per unit and see what is in effect.</summary>
public sealed class PoliciesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddScoped<IEffectivePolicyReader, EffectivePolicyReader>();

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapPolicyEndpoints();
}
