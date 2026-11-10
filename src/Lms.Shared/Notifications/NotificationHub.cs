namespace Lms.Shared.Notifications;

/// <summary>
/// SignalR hub contract (M1 Global Constraints). The WASM client passes the token in the <c>access_token</c>
/// query string for this path only.
/// </summary>
public static class NotificationHub
{
    public const string Path = "/hubs/notifications";

    /// <summary>Client method that receives a <see cref="NotificationDto"/>.</summary>
    public const string ReceiveMethod = "notification";
}
