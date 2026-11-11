using Lms.Infrastructure.Configuration;

namespace Lms.Api.Features.Settings;

/// <summary>The settings panel (FR-ORG-11): inheritable settings, locks and feature flags per unit.</summary>
public static class SettingsModule
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddScoped<IEffectiveSettingsReader, EffectiveSettingsReader>();
        return services;
    }
}
