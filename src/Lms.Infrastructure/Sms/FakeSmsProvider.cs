using System.Collections.Concurrent;
using Lms.Domain.Abstractions;
using Lms.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Sms;

/// <summary>An SMS the fake accepted: the rendered text, as a real gateway would have sent it.</summary>
public sealed record SentSms(string To, string TemplateKey, string Text, DateTime SentAtUtc);

/// <summary>
/// The development and test SMS provider (D68): validates and renders exactly like a gateway adapter would, keeps the messages in memory and logs one line with the
/// number masked. It sends nothing. A Saudi-licensed gateway adapter replaces it before go-live; the owner picks the gateway and registers the sender ID.
/// </summary>
public sealed class FakeSmsProvider(ILogger<FakeSmsProvider> logger, IClock clock) : ISmsProvider
{
    private readonly ConcurrentQueue<SentSms> _sent = new();

    public IReadOnlyList<SentSms> Sent => _sent.ToArray();

    public Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        var text = SmsTemplates.Render(message);
        _sent.Enqueue(new SentSms(message.To, message.TemplateKey, text, clock.UtcNow));
        logger.LogInformation("SMS not sent (Sms:Provider=Fake): to {Recipient}, template {Template}", Masking.Phone(message.To), message.TemplateKey);
        return Task.FromResult(new SmsSendResult($"fake-{Guid.NewGuid():N}"));
    }
}
