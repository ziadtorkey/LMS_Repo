using Lms.Domain.Common;

namespace Lms.Domain.Notifications;

/// <summary>
/// A person's choice to receive one event on one channel. Preferences belong to the user, not to a client: users are
/// platform-level (one person can hold roles in several clients), so this table is deliberately not tenant-owned and
/// every read and write is keyed by the caller's own UserId.
/// </summary>
public sealed class NotificationPreference : Entity
{
    private NotificationPreference()
    {
        EventKey = Channel = string.Empty;
    }

    public NotificationPreference(Guid userId, string eventKey, string channel, bool enabled)
    {
        UserId = userId;
        EventKey = eventKey;
        Channel = channel;
        Enabled = enabled;
    }

    public Guid UserId { get; private set; }
    public string EventKey { get; private set; }
    public string Channel { get; private set; }
    public bool Enabled { get; private set; }
}
