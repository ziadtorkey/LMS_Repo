namespace Lms.Domain.Notifications;

/// <summary>
/// What to publish. <c>UserIds</c> names the recipients; when empty, the event's default recipient roles are resolved at
/// <c>OrgUnitPath</c> and below. The acting user is never notified unless <c>NotifyActor</c> is set (a background job
/// that reports back to the user it runs for). <c>Urgent</c> (an urgent announcement, D76) skips quiet hours and caps on email and push, as a
/// mandatory event always does. <c>SourceId</c> names the thing the notice is about (an announcement), so its author can count what was delivered. <c>DeepLink</c> must be an app-relative path (starts with "/").
/// </summary>
public sealed record NotificationRequest(
    string EventKey,
    Guid? OrganizationId,
    IReadOnlyList<Guid> UserIds,
    IReadOnlyDictionary<string, string> Parameters,
    string? DeepLink = null,
    string? OrgUnitPath = null,
    bool NotifyActor = false,
    bool Urgent = false,
    Guid? SourceId = null);
