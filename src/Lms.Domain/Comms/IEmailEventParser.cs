namespace Lms.Domain.Comms;

public enum EmailEventKind
{
    Delivered,
    SoftBounce,
    HardBounce,
    Complaint,
}

/// <summary>One event of the relay. <c>EventId</c> is the relay's own id: the same id applies once (receipt table). <c>Address</c> is hashed by the processor before it is stored.</summary>
public sealed record EmailEvent(string EventId, EmailEventKind Kind, string ProviderMessageId, string? Address, DateTime OccurredAtUtc);

public enum EmailEventParseStatus
{
    Ok,

    /// <summary>The signature is missing or wrong: 401.</summary>
    BadSignature,

    /// <summary>The timestamp is older or newer than the tolerance: 401 (replay).</summary>
    Stale,

    /// <summary>The body is larger than the limit: 413.</summary>
    TooLarge,

    /// <summary>The body is not the expected shape: 400.</summary>
    Malformed,
}

public sealed record EmailEventParseResult(EmailEventParseStatus Status, IReadOnlyList<EmailEvent> Events)
{
    public static EmailEventParseResult Failed(EmailEventParseStatus status) => new(status, []);
}

/// <summary>
/// Verifies and reads the relay's bounce and complaint webhook (D67). The concrete mapping of a production relay (SES, SendGrid, Mailgun) is a go-live item;
/// the fake relay shape is what M6 builds against. Verification comes first: nothing of an unsigned body is read.
/// </summary>
public interface IEmailEventParser
{
    /// <summary>The largest body accepted, in bytes.</summary>
    int MaxBodyBytes { get; }

    EmailEventParseResult Parse(string? timestampHeader, string? signatureHeader, ReadOnlySpan<byte> body, DateTime nowUtc);
}
