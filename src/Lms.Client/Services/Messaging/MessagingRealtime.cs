using Lms.Shared;
using Lms.Shared.Messaging;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// The hub of messaging events as the client sees it (M6 Task 23, FR-MSG-01, FR-NOT-02). An event carries ids only: the client reads the message through the API (the server checks
/// that the caller is a participant) before anything is shown. The SignalR binding is <c>NotificationConnection</c> (method names: <c>NotificationHub</c> of the contract).
/// </summary>
public interface IMessagingHub
{
    /// <summary>A message arrived in a conversation of the caller: ids only.</summary>
    event Action<Guid, Guid>? MessageReceived;

    /// <summary>The members or the state (locked, read-only) of a conversation changed: reload them.</summary>
    event Action<Guid>? ConversationChanged;

    /// <summary>The caller read a conversation in another tab or device (ids only): its unread count is stale here.</summary>
    event Action<Guid>? ConversationRead;

    /// <summary>The connection dropped (true) or is up again (false).</summary>
    event Action<bool>? Disconnected;

    bool IsConnected { get; }
    Task StartAsync(Guid assignmentId);
    Task SwitchContextAsync(Guid assignmentId);
    Task StopAsync();
}

/// <summary>When to try again after the hub connection drops: at once, then 2, 5, 10 and 30 seconds, then every minute, never giving up while the page is open.</summary>
public static class RealtimeBackoff
{
    private static readonly TimeSpan[] Steps = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public static TimeSpan Next(int previousRetryCount) => previousRetryCount < Steps.Length ? Steps[previousRetryCount] : TimeSpan.FromMinutes(1);
}

/// <summary>
/// Realtime messaging on top of the hub: it turns an id-only event into a message read through the API, ignores one it has already shown, keeps the order the server gave (time, then
/// id) and says what a page must do: append to the open thread, count and announce a message of another thread once, reload after a reconnect, a change of members or a change of context.
/// </summary>
public sealed class MessagingRealtime : IAsyncDisposable
{
    private readonly IMessagingHub _hub;
    private readonly IMessagingApi _api;
    private readonly HashSet<Guid> _seen = [];
    private readonly HashSet<Guid> _announced = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly IContextStore? _context;

    public MessagingRealtime(IMessagingHub hub, IMessagingApi api, IContextStore? context = null)
    {
        _hub = hub;
        _api = api;
        _context = context;
        if (_context is not null) _context.Changed += OnContextChanged; // the notification feed moves the hub; this only forgets the old context
        _hub.MessageReceived += OnMessageReceived;
        _hub.ConversationChanged += OnConversationChanged;
        _hub.ConversationRead += OnConversationRead;
        _hub.Disconnected += OnDisconnected;
    }

    /// <summary>The conversation the person is reading now; null on the inbox.</summary>
    public Guid? OpenConversationId { get; set; }

    public bool IsConnected => _hub.IsConnected;

    /// <summary>A message of the open conversation, already read through the API: append it.</summary>
    public event Action<Guid, MessageDto>? MessageArrived;

    /// <summary>A message of another conversation: the unread counts changed; announced at most once until that conversation is read.</summary>
    public event Action<Guid, bool>? OtherThreadMessage;

    /// <summary>The members or the state of a conversation changed.</summary>
    public event Action<Guid>? ConversationChanged;

    /// <summary>The person read a conversation elsewhere: the badge and the list must drop its unread count.</summary>
    public event Action<Guid>? ConversationRead;

    /// <summary>After a reconnect: the newest page of the open conversation (merge it by id) and a request to reload the inbox.</summary>
    public event Action<Guid?, IReadOnlyList<MessageDto>>? Resynced;

    /// <summary>The connection state changed (true = connected).</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>The active context changed: everything shown belongs to the old one and must be cleared and reloaded.</summary>
    public event Action? ContextReset;

    public Task StartAsync(Guid assignmentId) => _hub.StartAsync(assignmentId);

    public async Task SwitchContextAsync(Guid assignmentId)
    {
        await _hub.SwitchContextAsync(assignmentId);
        ResetContext();
    }

    private void OnContextChanged() => ResetContext();

    private void ResetContext()
    {
        _seen.Clear();
        _announced.Clear();
        OpenConversationId = null;
        ContextReset?.Invoke();
    }

    /// <summary>A person read a conversation: its next message is announced again.</summary>
    public void MarkRead(Guid conversationId) => _announced.Remove(conversationId);

    /// <summary>Puts a page of messages into one list: no id twice, and the order of the server (creation time, then id), never the arrival order.</summary>
    public static List<MessageDto> Merge(IEnumerable<MessageDto> existing, IEnumerable<MessageDto> incoming)
    {
        var byId = new Dictionary<Guid, MessageDto>();
        foreach (var m in existing.Concat(incoming)) byId[m.Id] = m; // the later copy wins: it is the fresher one
        return [.. byId.Values.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)];
    }

    private void OnMessageReceived(Guid conversationId, Guid messageId) => _ = HandleMessageAsync(conversationId, messageId);

    private async Task HandleMessageAsync(Guid conversationId, Guid messageId)
    {
        if (!_seen.Add(messageId)) return; // the same id twice (a retry, a resync): ignored
        if (conversationId != OpenConversationId)
        {
            // another thread: only the counts change and one announcement per conversation, nothing of the text is fetched or shown
            var announce = _announced.Add(conversationId);
            OtherThreadMessage?.Invoke(conversationId, announce);
            return;
        }

        await _gate.WaitAsync();
        try
        {
            var page = await _api.GetMessagesAsync(conversationId, new MessageQuery());
            var message = page.Items.FirstOrDefault(m => m.Id == messageId);
            if (message is null)
            {
                _seen.Remove(messageId); // not readable (not a participant, or already off the first page): never shown from the event alone
                return;
            }

            MessageArrived?.Invoke(conversationId, message);
        }
        catch (Exception ex) when (ex is Lms.Client.Services.Api.ApiProblemException or HttpRequestException)
        {
            _seen.Remove(messageId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnConversationChanged(Guid conversationId) => ConversationChanged?.Invoke(conversationId);

    private void OnConversationRead(Guid conversationId)
    {
        _announced.Remove(conversationId); // read elsewhere: its next message is news again
        ConversationRead?.Invoke(conversationId);
    }

    private void OnDisconnected(bool disconnected)
    {
        ConnectionChanged?.Invoke(!disconnected);
        if (!disconnected) _ = ResyncAsync();
    }

    private async Task ResyncAsync()
    {
        IReadOnlyList<MessageDto> latest = [];
        try
        {
            if (OpenConversationId is { } id) latest = (await _api.GetMessagesAsync(id, new MessageQuery())).Items;
        }
        catch (Exception ex) when (ex is Lms.Client.Services.Api.ApiProblemException or HttpRequestException)
        {
            // the page will reload through the inbox: nothing was missed that a reload does not bring back
        }

        foreach (var m in latest) _seen.Add(m.Id);
        Resynced?.Invoke(OpenConversationId, latest);
    }

    public async ValueTask DisposeAsync()
    {
        _hub.MessageReceived -= OnMessageReceived;
        _hub.ConversationChanged -= OnConversationChanged;
        _hub.ConversationRead -= OnConversationRead;
        _hub.Disconnected -= OnDisconnected;
        if (_context is not null) _context.Changed -= OnContextChanged;
        await _hub.StopAsync();
        _gate.Dispose();
    }
}

/// <summary>The hub of the standalone server and of the tests: events on demand.</summary>
public sealed class FakeMessagingHub : IMessagingHub
{
    public event Action<Guid, Guid>? MessageReceived;
    public event Action<Guid>? ConversationChanged;
    public event Action<Guid>? ConversationRead;
    public event Action<bool>? Disconnected;

    public bool IsConnected { get; private set; }
    public Guid? Context { get; private set; }
    public List<Guid> Switched { get; } = [];

    public Task StartAsync(Guid assignmentId)
    {
        IsConnected = true;
        Context = assignmentId;
        return Task.CompletedTask;
    }

    public Task SwitchContextAsync(Guid assignmentId)
    {
        Switched.Add(assignmentId);
        Context = assignmentId;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public void Receive(Guid conversationId, Guid messageId) => MessageReceived?.Invoke(conversationId, messageId);
    public void Change(Guid conversationId) => ConversationChanged?.Invoke(conversationId);
    public void Read(Guid conversationId) => ConversationRead?.Invoke(conversationId);

    public void Drop()
    {
        IsConnected = false;
        Disconnected?.Invoke(true);
    }

    public void Reconnect()
    {
        IsConnected = true;
        Disconnected?.Invoke(false);
    }
}
