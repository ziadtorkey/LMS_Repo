namespace Lms.Domain.Notifications;

/// <summary>
/// What to publish. <c>UserIds</c> names the recipients; when empty, the event's default recipient roles are resolved at
/// <c>OrgUnitPath</c> and below. <c>DeepLink</c> must be an app-relative path (starts with "/").
/// </summary>
public sealed record NotificationRequest(
    string EventKey,
    Guid? OrganizationId,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyDictionary<string, string> Parameters,
    string? DeepLink = null,
    string? OrgUnitPath = null);
