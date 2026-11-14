using Lms.Client.Services.Api;
using Microsoft.AspNetCore.Components;

namespace Lms.Client.Services.Messaging;

/// <summary>
/// Client state of messaging: how many <b>conversations</b> have unread messages (the nav badge; the notification bell is separate).
/// The server owns the number; a hub event or a read only asks for it again, or hands over the new total.
/// </summary>
public interface IMessagingState
{
    int UnreadConversations { get; }
    event Action? Changed;
    Task RefreshAsync();
    /// <summary>Takes a total the caller already knows (a hub event carrying it).</summary>
    void Apply(int unreadConversations);
}

public sealed class MessagingState(IMessagingApi api, IContextStore context, NavigationManager navigation) : IMessagingState
{
    public int UnreadConversations { get; private set; }
    public event Action? Changed;

    public async Task RefreshAsync()
    {
        if (context.ActiveAssignmentId is null) { Apply(0); return; }
        var (total, failed) = await ApiCalls.RunAsync(() => api.GetUnreadTotalAsync(), navigation);
        if (!failed && total is not null) Apply(total.Conversations);
    }

    public void Apply(int unreadConversations)
    {
        UnreadConversations = Math.Max(0, unreadConversations);
        Changed?.Invoke();
    }
}
