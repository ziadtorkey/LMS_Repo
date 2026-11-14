using Lib.Net.Http.WebPush;
using Lms.Api.Infrastructure;
using Lms.Domain.Comms;
using Lms.Infrastructure.Comms.Push;
using Lms.Shared.Notifications;

namespace Lms.Api.Features.Comms;

/// <summary>
/// Web Push (M6 Task 8): the subscription endpoints and, outside the fake-channel hosts, the VAPID provider and the push channel. With
/// <c>Comms:FakeChannels</c> true (Development, tests) the fake channel of the outbox kernel stays the only push channel and <see cref="FakePushProvider"/>
/// stands in for the provider. The VAPID private key comes from user-secrets or the environment, never from a committed file.
/// </summary>
public sealed class PushModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PushOptions>(configuration.GetSection(PushOptions.Section));
        services.AddSingleton<PushKeyProtector>();
        if (configuration.GetValue("Comms:FakeChannels", false))
        {
            services.AddSingleton<FakePushProvider>();
            services.AddSingleton<IPushProvider>(sp => sp.GetRequiredService<FakePushProvider>());
            return;
        }

        services.AddHttpClient<PushServiceClient>().ConfigurePrimaryHttpMessageHandler(PushEndpointPolicy.CreateHandler);
        services.AddScoped<IPushProvider, VapidPushProvider>();
        services.AddScoped<IDeliveryChannel, PushDeliveryChannel>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapPushSubscriptionEndpoints();
}
