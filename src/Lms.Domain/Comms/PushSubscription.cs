using System.Security.Cryptography;
using System.Text;
using Lms.Domain.Common;

namespace Lms.Domain.Comms;

/// <summary>
/// A Web Push subscription of one browser of one user in one client (M6 Task 8). The endpoint is a capability URL, so it is never listed; the two keys are
/// stored protected (Data Protection) and the endpoint is unique per client by its SHA-256. A row is deleted when the push service answers 404 or 410.
/// </summary>
public sealed class PushSubscription : Entity, ITenantOwned
{
    public const int MaxEndpointLength = 600;
    public const int MaxKeyLength = 200;
    public const int MaxLabelLength = 100;

    private PushSubscription()
    {
        Endpoint = EndpointHash = ProtectedP256dh = ProtectedAuth = DeviceLabel = string.Empty;
    }

    public PushSubscription(Guid organizationId, Guid userId, string endpoint, string protectedP256dh, string protectedAuth, string deviceLabel, DateTime now)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Endpoint = endpoint;
        EndpointHash = HashOf(endpoint);
        ProtectedP256dh = protectedP256dh;
        ProtectedAuth = protectedAuth;
        DeviceLabel = deviceLabel;
        CreatedAtUtc = now;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string Endpoint { get; private set; }
    public string EndpointHash { get; private set; }
    public string ProtectedP256dh { get; private set; }
    public string ProtectedAuth { get; private set; }
    public string DeviceLabel { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastSuccessUtc { get; private set; }

    public void MarkSuccess(DateTime now) => LastSuccessUtc = now;

    public static string HashOf(string endpoint) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
}

/// <summary>The keys of one subscription, unprotected, for the moment of one send.</summary>
public sealed record PushTarget(string Endpoint, string P256dh, string Auth);

public enum PushResultKind
{
    Accepted,
    Gone,
    Transient,
    Permanent,
    NotConfigured,
}

public sealed record PushResult(PushResultKind Kind, string? ErrorCode = null, TimeSpan? RetryAfter = null);

/// <summary>Sends one payload to one subscription (VAPID). It never throws for a delivery problem; it answers with a <see cref="PushResult"/>.</summary>
public interface IPushProvider
{
    string ProviderName { get; }

    /// <summary>The application server public key (base64url), or null when Web Push is not configured.</summary>
    string? PublicKey { get; }

    Task<PushResult> SendAsync(PushTarget target, byte[] payload, TimeSpan timeToLive, bool urgent, CancellationToken cancellationToken);
}
