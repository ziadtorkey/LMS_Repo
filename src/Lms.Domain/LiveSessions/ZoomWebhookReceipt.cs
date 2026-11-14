using Lms.Domain.Common;

namespace Lms.Domain.LiveSessions;

/// <summary>
/// One processed Zoom webhook event (D54): the event id and when it was handled, so a re-delivery is a no-op. A platform table by design:
/// it carries no organization, session, user or payload (an event id is an opaque Zoom string), so there is nothing of any client in it.
/// Rows older than 30 days are purged.
/// </summary>
public sealed class ZoomWebhookReceipt : Entity
{
    private ZoomWebhookReceipt()
    {
    }

    public ZoomWebhookReceipt(string eventId, DateTime receivedAtUtc)
    {
        EventId = eventId;
        ReceivedAtUtc = receivedAtUtc;
    }

    public string EventId { get; private set; } = "";

    public DateTime ReceivedAtUtc { get; private set; }
}
