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
        Guid? organizationId, Guid userId, string eventKey, string parametersJson, string? deepLink, DateTime createdAt)
    {
        OrganizationId = organizationId;
        UserId = userId;
        EventKey = eventKey;
        ParametersJson = parametersJson;
        DeepLink = deepLink;
        CreatedAt = createdAt;
    }

    public Guid? OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string EventKey { get; private set; }
    public string ParametersJson { get; private set; }
    public string? DeepLink { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? ReadAt { get; private set; }

    public void MarkRead(DateTime utcNow) => ReadAt ??= utcNow;
}
