using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Configuration;

public static class ConfigurationServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsAndPolicies(this IServiceCollection services)
    {
        services.AddScoped<ISettingsResolver, SettingsResolver>();
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        services.AddScoped<IPolicyManager, PolicyManager>();
        return services;
    }
}
