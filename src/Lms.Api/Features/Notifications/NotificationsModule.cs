using Lms.Infrastructure.Notifications;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Lms.Api.Features.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<INotificationPusher, SignalRNotificationPusher>();
        services.AddSingleton<Lms.Infrastructure.Messaging.IMessagingRealtime, SignalRMessagingRealtime>();
        services.AddTransient<IStartupFilter, HubAccessTokenStartupFilter>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHub<NotificationsHub>(NotificationHub.Path).RequireAuthorization();
        app.MapMyNotificationEndpoints();
        app.MapMyPreferenceEndpoints();
        return app;
    }
}
