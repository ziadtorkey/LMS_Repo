using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Notifications;

/// <summary>
/// Delivers a stored notification to the user live (SignalR in the API host), only on the user's connections that
/// are working in the notification's client (<c>organizationId</c>; null = a notification that belongs to no client).
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, Guid? organizationId, NotificationDto notification, CancellationToken cancellationToken);
}
