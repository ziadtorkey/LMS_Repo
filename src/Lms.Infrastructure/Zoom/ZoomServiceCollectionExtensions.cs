using Lms.Domain.Abstractions;
using Lms.Infrastructure.Jobs;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lms.Infrastructure.Zoom;

public static class ZoomServiceCollectionExtensions
{
    private const string TokenClient = "zoom-token";

    /// <summary>
    /// Registers the Zoom pieces from the <c>Zoom</c> configuration section. Nothing here calls Zoom at startup; an unconfigured
    /// platform simply reports <c>Configured = false</c> through <see cref="ZoomProviderStatus"/>. The host allocator needs an
    /// <see cref="IHostSlotStore"/> from the live-session module and is resolved only when a session is created.
    /// </summary>
    public static IServiceCollection AddZoom(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ZoomOptions>(configuration.GetSection(ZoomOptions.Section));

        services.AddHttpClient(TokenClient);
        services.AddSingleton(sp => new ZoomTokenCache(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(TokenClient), sp.GetRequiredService<IOptions<ZoomOptions>>(), sp.GetRequiredService<IClock>()));
        services.AddSingleton(sp => new ZoomOutboundLimiter(sp.GetRequiredService<IClock>()));
        services.AddTransient<ZoomApiHandler>();
        services.AddHttpClient<ZoomApiClient>((sp, client) =>
                client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<ZoomOptions>>().Value.ApiBaseUrl))
            .AddHttpMessageHandler<ZoomApiHandler>();
        services.AddSingleton<ZoomProviderStatus>();

        services.AddScoped(sp => new ZoomHostAllocator(
            sp.GetRequiredService<IOptions<ZoomOptions>>(), sp.GetRequiredService<IJobKeyLock>(), sp.GetRequiredService<IHostSlotStore>(),
            () => HostSharePercent(configuration)));
        return services;
    }

    /// <summary>The platform-level per-client host share (Decision 2), clamped to its contract range; server configuration only.</summary>
    public static int HostSharePercent(IConfiguration configuration) =>
        Math.Clamp(
            configuration.GetValue(LiveSessionLimits.MaxHostSharePercentConfigKey, LiveSessionLimits.MaxHostSharePercentDefault),
            LiveSessionLimits.MaxHostSharePercentMin, LiveSessionLimits.MaxHostSharePercentMax);
}
