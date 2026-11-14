namespace Lms.Domain.Comms;

public enum DeliveryOutcomeKind
{
    Accepted,
    Transient,
    Permanent,
    NotConfigured,
}

/// <summary>What one channel said about one send. Only a code is ever kept (never a response body, an address or a token).</summary>
public sealed record DeliveryOutcome(DeliveryOutcomeKind Kind, string? ProviderMessageId = null, string? ErrorCode = null, TimeSpan? RetryAfter = null)
{
    public static DeliveryOutcome Accepted(string? providerMessageId = null) => new(DeliveryOutcomeKind.Accepted, providerMessageId);

    public static DeliveryOutcome Transient(string code, TimeSpan? retryAfter = null) => new(DeliveryOutcomeKind.Transient, null, code, retryAfter);

    /// <param name="suppress">The address is invalid or refused for good: the dispatcher records a suppression so it is never tried again.</param>
    public static DeliveryOutcome Permanent(string code, bool suppress = false) => new(DeliveryOutcomeKind.Permanent, null, code) { SuppressAddress = suppress };

    public static DeliveryOutcome NotConfigured() => new(DeliveryOutcomeKind.NotConfigured, null, "not_configured");

    public bool SuppressAddress { get; init; }
}
