using Lms.Shared;
using Lms.Shared.Notifications;

namespace Lms.Client.Services.Api;

/// <summary>The caller's in-app notifications and delivery preferences (FR-NOT-01, FR-NOT-02).</summary>
public interface INotificationsApi
{
    Task<NotificationPageDto> GetPageAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default);
    Task MarkReadAsync(Guid id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(CancellationToken ct = default);
    Task SavePreferencesAsync(SetNotificationPreferencesRequest request, CancellationToken ct = default);
}

public sealed class NotificationsApi(ApiHttp http) : INotificationsApi
{
    public async Task<NotificationPageDto> GetPageAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default) =>
        await http.GetAsync<NotificationPageDto>(
            $"{Routes.MyNotifications}?unreadOnly={unreadOnly.ToString().ToLowerInvariant()}&page={page}&pageSize={pageSize}", ct);

    public Task MarkReadAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, $"{Routes.MyNotifications}/{id}/read", null, ct);

    public Task MarkAllReadAsync(CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.MyNotificationsReadAll, null, ct);

    public async Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<NotificationPreferenceDto>>(Routes.MyNotificationPreferences, ct);

    public Task SavePreferencesAsync(SetNotificationPreferencesRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.MyNotificationPreferences, request, ct);
}

/// <summary>In-memory notifications for the standalone dev server and bUnit tests.</summary>
public sealed class FakeNotificationsApi : INotificationsApi
{
    private readonly List<NotificationDto> _items = [];
    private List<NotificationPreferenceDto> _preferences;

    public SetNotificationPreferencesRequest? LastSaved { get; private set; }
    /// <summary>When set, the next call throws it (tests simulate failures).</summary>
    public Exception? NextFailure { get; set; }
    public IReadOnlyList<NotificationDto> Items => _items;

    public FakeNotificationsApi()
    {
        _preferences = NotificationEvents.All
            .SelectMany(e => NotificationChannels.All.Select(c =>
                new NotificationPreferenceDto(e.Key, c, c is NotificationChannels.InApp or NotificationChannels.Email, c == NotificationChannels.InApp)))
            .ToList();
        Add(NotificationEvents.AccessRoleAssigned, new() { ["RoleNameAr"] = "المعلم", ["RoleNameEn"] = "Teacher", ["OrgUnitNameAr"] = "الصف الأول", ["OrgUnitNameEn"] = "Grade 1" },
            "/assignments", read: false);
        Add(NotificationEvents.OrganizationPolicyChanged, new() { ["PolicyKey"] = "x", ["OrgUnitNameAr"] = "المدرسة الرئيسية", ["OrgUnitNameEn"] = "Main School", ["State"] = "On" },
            null, read: false);
        Add(NotificationEvents.AccessRoleRemoved, new() { ["RoleNameAr"] = "المعلم", ["RoleNameEn"] = "Teacher", ["OrgUnitNameAr"] = "الصف الأول", ["OrgUnitNameEn"] = "Grade 1" },
            null, read: true);
    }

    public NotificationDto Add(string eventKey, Dictionary<string, string> parameters, string? deepLink, bool read)
    {
        var dto = new NotificationDto(Guid.NewGuid(), eventKey, parameters, deepLink, DateTime.UtcNow.AddMinutes(-_items.Count * 11), read);
        _items.Insert(0, dto);
        return dto;
    }

    public Task<NotificationPageDto> GetPageAsync(bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        Fail();
        var rows = _items.Where(n => !unreadOnly || !n.IsRead).OrderByDescending(n => n.CreatedAt).ToList();
        return Task.FromResult(new NotificationPageDto(
            rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), rows.Count, _items.Count(n => !n.IsRead)));
    }

    public Task MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        Fail();
        var i = _items.FindIndex(n => n.Id == id);
        if (i >= 0) _items[i] = _items[i] with { IsRead = true };
        return Task.CompletedTask;
    }

    public Task MarkAllReadAsync(CancellationToken ct = default)
    {
        Fail();
        for (var i = 0; i < _items.Count; i++) _items[i] = _items[i] with { IsRead = true };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationPreferenceDto>> GetPreferencesAsync(CancellationToken ct = default)
    {
        Fail();
        return Task.FromResult<IReadOnlyList<NotificationPreferenceDto>>(_preferences.ToList());
    }

    public Task SavePreferencesAsync(SetNotificationPreferencesRequest request, CancellationToken ct = default)
    {
        Fail();
        LastSaved = request;
        _preferences = [.. request.Preferences];
        return Task.CompletedTask;
    }

    private void Fail()
    {
        if (NextFailure is not { } ex) return;
        NextFailure = null;
        throw ex;
    }
}
