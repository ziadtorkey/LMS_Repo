namespace Lms.Shared.Notifications;

/// <summary>
/// SignalR hub contract (M1 Global Constraints; M6 L13). The WASM client passes the token in the <c>access_token</c>
/// query string for this path only. The server adds a connection to the caller's own groups on connect; the hub exposes
/// <b>no client-callable group join</b> and no method that takes a user or conversation id.
/// </summary>
public static class NotificationHub
{
    public const string Path = "/hubs/notifications";

    /// <summary>Client method that receives a <see cref="NotificationDto"/>.</summary>
    public const string ReceiveMethod = "notification";

    /// <summary>Server to client, ids only (<c>Messaging.MessageReceivedEvent</c>): a new message in a conversation the caller takes part in.</summary>
    public const string MessageReceivedMethod = "MessageReceived";

    /// <summary>Server to client, ids only (<c>ConversationReadEvent</c>): the caller's other tabs only; the conversation was read up to a message.</summary>
    public const string ConversationReadMethod = "ConversationRead";

    /// <summary>Server to client, ids only (<c>ConversationChangedEvent</c>): members, lock or read-only state changed; the client refetches the conversation.</summary>
    public const string ConversationChangedMethod = "ConversationChanged";

    /// <summary>Every method the server may invoke on a client. With the <c>Messaging</c> flag off for the recipient's context the three messaging methods are never sent.</summary>
    public static IReadOnlyList<string> ServerToClientMethods { get; } = [ReceiveMethod, MessageReceivedMethod, ConversationReadMethod, ConversationChangedMethod];

    /// <summary>Every method a client may invoke on the hub: only the context switch, which re-checks the caller's own assignment.</summary>
    public static IReadOnlyList<string> ClientCallableMethods { get; } = ["SwitchContext"];
}
