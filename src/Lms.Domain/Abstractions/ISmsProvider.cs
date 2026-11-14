namespace Lms.Domain.Abstractions;

/// <summary>
/// One SMS to send (D68, D66). It carries no free text: the body is rendered by the transport from a fixed template, so no student data, grade, attendance
/// record or message text can reach a gateway. <c>To</c> is an E.164 number; <c>Parameters</c> holds only the values the template allows; <c>Language</c> is
/// <c>ar</c> or <c>en</c> (the recipient's language, D66).
/// </summary>
public sealed record SmsMessage(string To, string TemplateKey, IReadOnlyDictionary<string, string> Parameters, string Language = "en");

/// <summary>The gateway's id for a sent message (the fake returns a generated one).</summary>
public sealed record SmsSendResult(string MessageId);

/// <summary>
/// The SMS transport (D68). Only a development/test fake exists; the owner picks a Saudi-licensed gateway before go-live and its adapter implements this
/// interface. A message that breaks the template rules throws <see cref="ArgumentException"/> before anything leaves the process.
/// </summary>
public interface ISmsProvider
{
    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
}
