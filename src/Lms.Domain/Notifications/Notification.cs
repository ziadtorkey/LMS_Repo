using Lms.Domain.Common;

namespace Lms.Domain.Notifications;

/// <summary>
/// One in-app notification of one user. A history row (never soft-deleted). OrganizationId is the client the event
/// happened in, null for platform-level events.
/// </summary>
public sealed class Notification : Entity, IOptionalTenantOwned
{
    private Notification()
    {
        EventKey = ParametersJson = string.Empty;
    }

    public Notification(
        Guid? organizationId, Guid userId, string eventKey, string parametersJson, string? deepLink, DateTime createdAt, Guid? sourceId = null)
    {
        OrganizationId = organizationId;
        UserId = userId;
        EventKey = eventKey;
        ParametersJson = parametersJson;
        DeepLink = deepLink;
        CreatedAt = createdAt;
        SourceId = sourceId;
    }

    public Guid? OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string EventKey { get; private set; }
    public string ParametersJson { get; private set; }
    public string? DeepLink { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    /// <summary>The thing the notice is about (an announcement id), for delivery counts; null for most events.</summary>
    public Guid? SourceId { get; private set; }

    public void MarkRead(DateTime utcNow) => ReadAt ??= utcNow;
}
