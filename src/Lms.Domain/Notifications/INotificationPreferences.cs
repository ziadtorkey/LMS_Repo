namespace Lms.Domain.Notifications;

public interface INotificationPreferences
{
    /// <summary>
    /// Whether the user wants the event on the channel: the saved choice, else the default (in-app on and locked, email on,
    /// SMS and push off). Used by the delivery channels (M6).
    /// </summary>
    Task<bool> IsEnabledAsync(Guid userId, string eventKey, string channel, CancellationToken cancellationToken = default);
}
