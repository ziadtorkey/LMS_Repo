using Lms.Domain.Grading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.Infrastructure.Grading.Scales;

public static class GradingScalesServiceCollectionExtensions
{
    public static IServiceCollection AddGradingScales(this IServiceCollection services)
    {
        services.AddScoped<IScaleResolver, ScaleResolver>();
        services.AddScoped<IPresetReconciler, PresetReconciler>();
        services.AddScoped<GradingScaleService>();
        services.TryAddSingleton<IScalePresetCatalog, SaudiPresetCatalog>();
        services.TryAddScoped<IScaleUsage, NoScaleUsage>();
        return services;
    }
}
