using System.Collections.Concurrent;
using Lms.Domain.Abstractions;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>Records every email instead of sending it.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>Simulates a mail outage.</summary>
    public bool Fail { get; set; }

    public IReadOnlyList<EmailMessage> Sent => _sent.ToArray();

    public IReadOnlyList<EmailMessage> To(string address) =>
        _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase)).ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("SMTP is down.");
        }

        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}
