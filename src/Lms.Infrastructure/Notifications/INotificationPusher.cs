using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Notifications;

/// <summary>Delivers a stored notification to the user live (SignalR in the API host).</summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, NotificationDto notification, CancellationToken cancellationToken);
}
