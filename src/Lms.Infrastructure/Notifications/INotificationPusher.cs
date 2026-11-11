using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Notifications;

/// <summary>
/// Delivers a stored notification to the user live (SignalR in the API host), only on the user's connections that
/// are working in the notification's client (<c>organizationId</c>; null = a notification that belongs to no client).
/// </summary>
public interface INotificationPusher
{
    Task PushAsync(Guid userId, Guid? organizationId, NotificationDto notification, CancellationToken cancellationToken);

    /// <summary>
    /// Delivers a whole fan-out. Implementations that check entitlements should do it for all recipients at once; the
    /// default simply pushes one by one.
    /// </summary>
    async Task PushManyAsync(IReadOnlyList<PendingPush> pushes, CancellationToken cancellationToken)
    {
        foreach (var push in pushes)
        {
            await PushAsync(push.UserId, push.OrganizationId, push.Notification, cancellationToken);
        }
    }
}

/// <summary>One notification waiting to be pushed to one user.</summary>
public sealed record PendingPush(Guid UserId, Guid? OrganizationId, NotificationDto Notification);
