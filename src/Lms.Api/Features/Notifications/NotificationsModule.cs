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
        services.AddTransient<IStartupFilter, HubAccessTokenStartupFilter>();
        return services;
    }

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHub<NotificationsHub>(NotificationHub.Path).RequireAuthorization();
        app.MapMyNotificationEndpoints();
        return app;
    }
}
