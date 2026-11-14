namespace Lms.Infrastructure.Comms.Push;

/// <summary>
/// The VAPID identity of the platform (<c>Comms:Push</c>). The private key is a secret: it comes from user-secrets or the environment, never from a
/// committed file (Development carries a placeholder with no key, so push is "not configured" until a developer generates one; see the runbook).
/// </summary>
public sealed class PushOptions
{
    public const string Section = "Comms:Push";

    /// <summary>A contact URL for the push services: <c>mailto:</c> or <c>https:</c>.</summary>
    public string Subject { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public string PrivateKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey) && !string.IsNullOrWhiteSpace(Subject);
}
