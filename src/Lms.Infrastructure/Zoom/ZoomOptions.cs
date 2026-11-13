namespace Lms.Infrastructure.Zoom;

/// <summary>
/// The one platform Zoom app (D49), bound from the <c>Zoom</c> configuration section. Secrets (client secret, webhook secret token)
/// live only in server configuration or the secret store: never in the database, DTOs, logs, audit records or the repository.
/// </summary>
public sealed class ZoomOptions
{
    public const string Section = "Zoom";

    public string AccountId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>Verifies webhook signatures and answers the URL-validation challenge (Task 7).</summary>
    public string WebhookSecretToken { get; set; } = "";

    /// <summary>The licensed platform host users (Zoom user ids or emails); the licence count is the pool size (D53).</summary>
    public string[] HostPool { get; set; } = [];

    /// <summary>Applied before the start and after the scheduled end of every session when allocating a host.</summary>
    public int HostBufferMinutes { get; set; } = 10;

    public string ApiBaseUrl { get; set; } = "https://api.zoom.us/v2/";
    public string TokenUrl { get; set; } = "https://zoom.us/oauth/token";

    /// <summary>True when the app credentials and at least one pool host are present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && HostPool.Length > 0;
}
