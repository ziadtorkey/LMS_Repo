namespace Lms.Domain.Notifications;

public interface INotificationPublisher
{
    /// <summary>Stores one row per recipient and pushes it live. The acting user is never notified.</summary>
    /// <exception cref="ArgumentException">
    /// Unknown event key, missing parameter, no recipients, or a deep link that is not an app-relative path.
    /// </exception>
    Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}
