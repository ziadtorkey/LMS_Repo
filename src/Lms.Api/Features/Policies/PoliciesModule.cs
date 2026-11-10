using Lms.Infrastructure.Configuration;

namespace Lms.Api.Features.Policies;

/// <summary>The policies control panel (FR-ORG-10): switch policies on per unit and see what is in effect.</summary>
public static class PoliciesModule
{
    public static IServiceCollection AddPolicies(this IServiceCollection services)
    {
        services.AddScoped<IEffectivePolicyReader, EffectivePolicyReader>();
        return services;
    }
}
