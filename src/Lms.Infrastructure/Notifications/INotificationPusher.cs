using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Notifications;

/// <summary>
/// Delivers a stored notification to the user live (SignalR in the API host), to the connections working in the same
/// client as the notification (null = platform-level, only connections without a client).
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, Guid? organizationId, NotificationDto notification, CancellationToken cancellationToken);
}
