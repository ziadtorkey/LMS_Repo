using Lms.Infrastructure.Notifications;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Lms.Api.Features.Notifications;

/// <summary>
/// Push channel of the in-app notification center. The server only pushes (<see cref="NotificationHub.ReceiveMethod"/>);
/// clients send nothing. Connecting needs an authenticated user (the route is mapped with RequireAuthorization).
/// </summary>
public sealed class NotificationsHub : Hub;

/// <summary>Targets a user by the "sub" claim of the token, the same id the notification rows are stored under.</summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.FindFirst("sub")?.Value;
}

public sealed class SignalRNotificationPusher(IHubContext<NotificationsHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync(NotificationHub.ReceiveMethod, notification, cancellationToken);
}
