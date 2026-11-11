using Lms.Shared.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;

namespace Lms.Client.Services;

/// <summary>
/// Realtime channel of the notification center. A connection is opened for one context (role assignment); when the
/// user changes context the same connection is moved with <see cref="SwitchContextAsync"/>. A connection without a
/// context receives no client notifications.
/// </summary>
public interface INotificationConnection
{
    event Action<NotificationDto>? Received;
    bool IsStarted { get; }
    Task StartAsync(Guid assignmentId);
    Task SwitchContextAsync(Guid assignmentId);
    Task StopAsync();
}

/// <summary>SignalR implementation: <c>/hubs/notifications?context=&lt;assignmentId&gt;</c>, token from the app's auth, auto-reconnect.</summary>
public sealed class NotificationConnection(
    NavigationManager navigation, IAccessTokenProvider tokens, IContextStore context) : INotificationConnection, IAsyncDisposable
{
    public const string SwitchContextMethod = "SwitchContext";

    private HubConnection? _hub;

    public event Action<NotificationDto>? Received;
    public bool IsStarted => _hub?.State is HubConnectionState.Connected or HubConnectionState.Reconnecting;

    public static string HubUrl(string baseUri, Guid assignmentId) =>
        $"{new Uri(new Uri(baseUri), NotificationHub.Path.TrimStart('/'))}?context={assignmentId}";

    public async Task StartAsync(Guid assignmentId)
    {
        if (_hub is not null)
        {
            await SwitchContextAsync(assignmentId);
            return;
        }

        var hub = new HubConnectionBuilder()
            .WithUrl(HubUrl(navigation.BaseUri, assignmentId), options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var result = await tokens.RequestAccessToken();
                    return result.TryGetToken(out var token) ? token.Value : null;
                };
            })
            .WithAutomaticReconnect()
            .Build();
        hub.On<NotificationDto>(NotificationHub.ReceiveMethod, n => Received?.Invoke(n));
        // After a reconnect the server has forgotten the context: say again which one is active.
        hub.Reconnected += _ => context.ActiveAssignmentId is { } id
            ? hub.InvokeAsync(SwitchContextMethod, id) : Task.CompletedTask;
        _hub = hub;
        await hub.StartAsync();
    }

    public async Task SwitchContextAsync(Guid assignmentId)
    {
        if (_hub?.State == HubConnectionState.Connected)
            await _hub.InvokeAsync(SwitchContextMethod, assignmentId);
    }

    public async Task StopAsync()
    {
        if (_hub is null) return;
        var hub = _hub;
        _hub = null;
        await hub.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}

/// <summary>Stand-in for the standalone dev server and bUnit tests: records the calls, pushes on demand.</summary>
public sealed class FakeNotificationConnection : INotificationConnection
{
    public event Action<NotificationDto>? Received;
    public bool IsStarted { get; private set; }
    /// <summary>Context the connection was opened with.</summary>
    public Guid? StartedWith { get; private set; }
    public List<Guid> Switched { get; } = [];
    public int StopCount { get; private set; }
    public Exception? StartFailure { get; set; }

    public Task StartAsync(Guid assignmentId)
    {
        if (StartFailure is { } ex) return Task.FromException(ex);
        if (IsStarted) return SwitchContextAsync(assignmentId);
        IsStarted = true;
        StartedWith = assignmentId;
        return Task.CompletedTask;
    }

    public Task SwitchContextAsync(Guid assignmentId)
    {
        Switched.Add(assignmentId);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsStarted = false;
        StopCount++;
        return Task.CompletedTask;
    }

    public void Push(NotificationDto notification) => Received?.Invoke(notification);
}
