using Lms.Domain.Common;

namespace Lms.Domain.Comms;

/// <summary>
/// One delivery of one notification on one channel to one user (M6 Task 5). Written in the same transaction as the in-app <c>Notification</c>, so a crash
/// loses no delivery and sends none for a notification that was rolled back. It holds <b>no address and no message text</b>: the dispatcher resolves the
/// address and renders the text at send time. A transactional email (account, reset) has <see cref="NotificationId"/> null and a <see cref="TemplateKey"/>.
/// A hold (quiet hours, a cap) is a <c>Pending</c> row with <see cref="HeldUntilUtc"/>; its time to live starts when the hold ends.
/// </summary>
public sealed class DeliveryOutbox : Entity, ITenantOwned
{
    private DeliveryOutbox()
    {
        Channel = Status = string.Empty;
    }

    public DeliveryOutbox(
        Guid organizationId, Guid? notificationId, Guid userId, string channel, string? templateKey, bool isPriority, DateTime now, DateTime? heldUntilUtc, TimeSpan timeToLive)
    {
        OrganizationId = organizationId;
        NotificationId = notificationId;
        UserId = userId;
        Channel = channel;
        TemplateKey = templateKey;
        IsPriority = isPriority;
        Status = DeliveryStates.Pending;
        CreatedAtUtc = now;
        HeldUntilUtc = heldUntilUtc;
        NextAttemptAtUtc = heldUntilUtc ?? now;
        ExpiresAtUtc = (heldUntilUtc ?? now) + timeToLive;
    }

    public Guid OrganizationId { get; private set; }
    public Guid? NotificationId { get; private set; }
    public Guid UserId { get; private set; }
    public string Channel { get; private set; }
    public string? TemplateKey { get; private set; }

    /// <summary>Urgent or mandatory: never held by quiet hours or a cap.</summary>
    public bool IsPriority { get; private set; }

    public string Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public DateTime? HeldUntilUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public string? Provider { get; private set; }
    public string? ProviderMessageId { get; private set; }

    /// <summary>A code only, never a response body, an address or a token.</summary>
    public string? LastErrorCode { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    public void MarkSent(DateTime now, string provider, string? providerMessageId)
    {
        Status = DeliveryStates.Sent;
        SentAtUtc = now;
        Provider = provider;
        ProviderMessageId = providerMessageId;
        LastErrorCode = null;
        LeaseUntilUtc = null;
        HeldUntilUtc = null;
    }

    /// <summary>Back to <c>Pending</c> at <paramref name="nextAttemptAtUtc"/> (a retry after a transient failure, or the lease of a crashed worker).</summary>
    public void Retry(DateTime nextAttemptAtUtc, string? errorCode)
    {
        Status = DeliveryStates.Pending;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastErrorCode = errorCode;
        LeaseUntilUtc = null;
    }

    /// <summary>The provider is not configured: wait without counting an attempt.</summary>
    public void WaitForProvider(DateTime nextAttemptAtUtc)
    {
        Attempts = Math.Max(0, Attempts - 1);
        Retry(nextAttemptAtUtc, "not_configured");
    }

    /// <summary>Waits until <paramref name="untilUtc"/>; the time to live starts then. A claimed row gives its attempt back (a hold is not an attempt).</summary>
    public void Hold(DateTime untilUtc, TimeSpan timeToLive)
    {
        if (Status == DeliveryStates.Sending)
        {
            Attempts = Math.Max(0, Attempts - 1);
        }

        Status = DeliveryStates.Pending;
        HeldUntilUtc = untilUtc;
        NextAttemptAtUtc = untilUtc;
        ExpiresAtUtc = untilUtc + timeToLive;
        LeaseUntilUtc = null;
    }

    public void Fail(string errorCode, string provider)
    {
        Status = DeliveryStates.Failed;
        LastErrorCode = errorCode;
        Provider = provider;
        LeaseUntilUtc = null;
    }

    public void Skip(string code)
    {
        Status = DeliveryStates.Skipped;
        LastErrorCode = code;
        LeaseUntilUtc = null;
    }

    public void Expire(string code)
    {
        Status = DeliveryStates.Expired;
        LastErrorCode = code;
        LeaseUntilUtc = null;
    }
}

/// <summary>The status strings of <c>DeliveryOutbox.Status</c> (the contract list is <c>Lms.Shared.Messaging.DeliveryStatuses</c>, which these equal).</summary>
public static class DeliveryStates
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
    public const string Expired = "Expired";
}

/// <summary>Codes kept in <c>LastErrorCode</c> for rows the kernel ends by its own rules.</summary>
public static class DeliveryCodes
{
    public const string Suppressed = "suppressed";
    public const string NoContact = "no_contact";
    public const string RecipientCap = "recipient_cap";
    public const string Expired = "expired";
    public const string AttemptsExhausted = "attempts_exhausted";
    public const string NoChannel = "no_channel";
    public const string ChannelDisabled = "channel_disabled";
    public const string PreferenceOff = "preference_off";
}
