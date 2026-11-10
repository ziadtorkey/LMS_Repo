namespace Lms.Shared.Notifications;

/// <summary>
/// An in-app notification. The client renders the text from resources by <c>EventKey</c> with <c>Parameters</c>
/// (both languages are supplied, e.g. <c>RoleNameAr</c> / <c>RoleNameEn</c>).
/// </summary>
public sealed record NotificationDto(
    Guid Id,
    string EventKey,
    IReadOnlyDictionary<string, string> Parameters,
    string? DeepLink,
    DateTime CreatedAt,
    bool IsRead);

/// <summary>Response of GET /api/me/notifications.</summary>
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Total, int UnreadCount);

/// <summary>One event x channel preference of the caller; <c>Locked</c> = cannot be turned off (InApp).</summary>
public sealed record NotificationPreferenceDto(string EventKey, string Channel, bool Enabled, bool Locked);

/// <summary>Body of PUT /api/me/notification-preferences: replaces the caller's preferences.</summary>
public sealed record SetNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceDto> Preferences);
