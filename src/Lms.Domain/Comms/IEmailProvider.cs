namespace Lms.Domain.Comms;

/// <summary>One email ready to leave: both bodies are rendered, the address is the only recipient. No notification text beyond the template reaches it (M6 L22).</summary>
public sealed record EmailEnvelope(string To, string Subject, string HtmlBody, string TextBody, string? CorrelationId = null);

/// <summary>What the relay said about one send. Only a code is ever kept, never a body or an address (M6 Task 5/6).</summary>
public enum EmailSendStatus
{
    /// <summary>The relay took the message (nothing is promised about delivery).</summary>
    Accepted,

    /// <summary>Try again later: a network error or a 4xx answer.</summary>
    Transient,

    /// <summary>Never try again: the address or the message is refused (5xx).</summary>
    Permanent,

    /// <summary>No relay is configured: the row waits (long back-off) until one is.</summary>
    NotConfigured,
}

public sealed record EmailSendResult(EmailSendStatus Status, string? ProviderMessageId = null, string? ErrorCode = null, TimeSpan? RetryAfter = null)
{
    public static EmailSendResult Accepted(string? providerMessageId = null) => new(EmailSendStatus.Accepted, providerMessageId);

    public static EmailSendResult Transient(string code, TimeSpan? retryAfter = null) => new(EmailSendStatus.Transient, null, code, retryAfter);

    public static EmailSendResult Permanent(string code) => new(EmailSendStatus.Permanent, null, code);

    public static EmailSendResult NotConfigured() => new(EmailSendStatus.NotConfigured, null, "not_configured");
}

/// <summary>
/// Sends one rendered email through the configured relay (D67: MailKit over SMTP, or the development fake). It never throws for a delivery problem; it
/// answers with a status the dispatcher maps to retry or stop.
/// </summary>
public interface IEmailProvider
{
    Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken);
}
