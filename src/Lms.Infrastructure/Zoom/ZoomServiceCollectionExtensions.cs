using Lms.Domain.Abstractions;
using Lms.Domain.LiveSessions;
using Lms.Infrastructure.LiveSessions;
using Lms.Infrastructure.Jobs;
using Lms.Shared.LiveSessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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

        // The provider live sessions talk to: Zoom, except that a Development host without Zoom credentials gets the in-memory fake so
        // authoring works offline. Any other host without credentials gets the Zoom provider, which answers provider_not_configured.
        services.AddSingleton<FakeMeetingProvider>();
        services.AddScoped<ZoomMeetingProvider>();
        // Replaces the "no provider" default the live-session module registers.
        services.Replace(ServiceDescriptor.Scoped<IMeetingProvider>(sp =>
            !sp.GetRequiredService<IOptions<ZoomOptions>>().Value.IsConfigured && sp.GetService<IHostEnvironment>()?.IsDevelopment() == true
                ? sp.GetRequiredService<FakeMeetingProvider>()
                : sp.GetRequiredService<ZoomMeetingProvider>()));
        services.AddScoped<IHostSlotStore, EfHostSlotStore>();
        services.AddScoped<ZoomSyncSweepJob>();
        return services;
    }

    /// <summary>The platform-level per-client host share (Decision 2), clamped to its contract range; server configuration only.</summary>
    public static int HostSharePercent(IConfiguration configuration) =>
        Math.Clamp(
            configuration.GetValue(LiveSessionLimits.MaxHostSharePercentConfigKey, LiveSessionLimits.MaxHostSharePercentDefault),
            LiveSessionLimits.MaxHostSharePercentMin, LiveSessionLimits.MaxHostSharePercentMax);
}
