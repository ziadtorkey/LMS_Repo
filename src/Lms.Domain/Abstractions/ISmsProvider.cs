namespace Lms.Domain.Abstractions;

/// <summary>
/// One SMS to send (D68, D66). It carries no free text: the body is rendered by the transport from a fixed template, so no student data, grade, attendance
/// record or message text can reach a gateway. <c>To</c> is an E.164 number; <c>Parameters</c> holds only the values the template allows; <c>Language</c> is
/// <c>ar</c> or <c>en</c> (the recipient's language, D66). D72: SMS goes only to adults, about themselves. The channel resolver sets
/// <c>RecipientIsMinor</c> (the person who would receive it is under 18) and <c>ConcernsAnotherPerson</c> (it is news about someone else, for example a child);
/// <see cref="GuardedSmsProvider"/> refuses either with <see cref="SmsRefusal.NotAllowedForMinor"/>, so the rule holds even if a resolver forgets to choose email or push.
/// D75: guardians never receive SMS, not even urgent school-wide announcements; the resolver sets <c>RecipientIsGuardian</c> for a guardian account and the guard refuses it
/// with <see cref="SmsRefusal.NotAllowedForGuardian"/>.
/// </summary>
public sealed record SmsMessage(
    string To, string TemplateKey, IReadOnlyDictionary<string, string> Parameters, string Language = "en",
    bool RecipientIsMinor = false, bool ConcernsAnotherPerson = false, bool RecipientIsGuardian = false);

/// <summary>The refusal codes of the SMS channel (the same values the channel resolver returns).</summary>
public static class SmsRefusal
{
    /// <summary>D72: no SMS to a minor and no SMS about a minor; guardians get news about their children by email and push.</summary>
    public const string NotAllowedForMinor = "sms_not_allowed_for_minor";

    /// <summary>D75: no SMS to a guardian account, whatever the announcement; guardians are reached by email and push.</summary>
    public const string NotAllowedForGuardian = "sms_not_allowed_for_guardian";
}

/// <summary>The SMS was refused by a channel rule; <see cref="Code"/> is one of <see cref="SmsRefusal"/>.</summary>
public sealed class SmsRefusedException : InvalidOperationException
{
    public SmsRefusedException(string code)
        : base(code) => Code = code;

    public string Code { get; }
}

/// <summary>The gateway's id for a sent message (the fake returns a generated one).</summary>
public sealed record SmsSendResult(string MessageId);

/// <summary>
/// The SMS transport (D68). Only a development/test fake exists; the owner picks a gateway before go-live (D73: in-Kingdom or GCC providers preferred) and its adapter implements this
/// interface. A message that breaks the template rules throws <see cref="ArgumentException"/> before anything leaves the process.
/// </summary>
public interface ISmsProvider
{
    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
