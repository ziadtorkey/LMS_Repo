using Lms.Client.Services.Api;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Services;

/// <summary>
/// Client state of the notification center: the latest notifications, the unread count and the realtime connection,
/// which follows the active context (opened with it, moved with <c>SwitchContext</c> when it changes, closed on logout).
/// </summary>
public interface INotificationFeed
{
    IReadOnlyList<NotificationDto> Recent { get; }
    int UnreadCount { get; }
    /// <summary>Raised for every notification that arrives live (the bell shows a snackbar).</summary>
    event Action<NotificationDto>? Arrived;
    event Action? Changed;
    Task StartAsync();
    Task RefreshAsync();
    Task MarkReadAsync(Guid id);
    Task MarkAllReadAsync();
}

public sealed class NotificationFeed : INotificationFeed, IDisposable
{
    public const int RecentCount = 10;

    private readonly INotificationsApi _api;
    private readonly INotificationConnection _connection;
    private readonly IContextStore _context;
    private readonly NavigationManager _navigation;
    private List<NotificationDto> _recent = [];

    public NotificationFeed(INotificationsApi api, INotificationConnection connection, IContextStore context, NavigationManager navigation)
    {
        _api = api; _connection = connection; _context = context; _navigation = navigation;
        _connection.Received += OnReceived;
        _context.Changed += OnContextChanged;
    }

    public IReadOnlyList<NotificationDto> Recent => _recent;
    public int UnreadCount { get; private set; }
    public event Action<NotificationDto>? Arrived;
    public event Action? Changed;

    public async Task StartAsync()
    {
        await RefreshAsync();
        await ConnectAsync();
    }

    public async Task RefreshAsync()
    {
        if (_context.ActiveAssignmentId is null)
        {
            _recent = []; UnreadCount = 0;
            Changed?.Invoke();
            return;
        }
        var (page, _) = await ApiCalls.RunAsync(() => _api.GetPageAsync(false, 1, RecentCount), _navigation);
        if (page is not null)
        {
            _recent = [.. page.Items];
            UnreadCount = page.UnreadCount;
        }
        Changed?.Invoke();
    }

    public async Task MarkReadAsync(Guid id)
    {
        var i = _recent.FindIndex(n => n.Id == id);
        if (i >= 0 && !_recent[i].IsRead)
        {
            _recent[i] = _recent[i] with { IsRead = true };
            UnreadCount = Math.Max(0, UnreadCount - 1);
            Changed?.Invoke();
        }
        await ApiCalls.RunAsync(async () => { await _api.MarkReadAsync(id); return true; }, _navigation);
    }

    public async Task MarkAllReadAsync()
    {
        var (_, failed) = await ApiCalls.RunAsync(async () => { await _api.MarkAllReadAsync(); return true; }, _navigation);
        if (failed) return;
        _recent = _recent.Select(n => n with { IsRead = true }).ToList();
        UnreadCount = 0;
        Changed?.Invoke();
    }

    private async Task ConnectAsync()
    {
        if (_context.ActiveAssignmentId is not { } assignment) return;
        try { await _connection.StartAsync(assignment); }
        catch (Exception) { } // offline or hub unreachable: the list still works, the bell just is not live
    }

    private void OnReceived(NotificationDto notification)
    {
        _recent.Insert(0, notification);
        if (_recent.Count > RecentCount) _recent.RemoveRange(RecentCount, _recent.Count - RecentCount);
        if (!notification.IsRead) UnreadCount++;
        Changed?.Invoke();
        Arrived?.Invoke(notification);
    }

    private void OnContextChanged() => _ = FollowContextAsync();

    private async Task FollowContextAsync()
    {
        if (_context.ActiveAssignmentId is not { } assignment)
        {
            await _connection.StopAsync();
            await RefreshAsync();
            return;
        }
        if (_connection.IsStarted)
            await SafeAsync(() => _connection.SwitchContextAsync(assignment));
        else
            await ConnectAsync();
        await RefreshAsync();
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try { await action(); } catch (Exception) { }
    }

    public void Dispose()
    {
        _connection.Received -= OnReceived;
        _context.Changed -= OnContextChanged;
    }
}
