using System.Collections.Concurrent;
using Lms.Domain.Comms;

namespace Lms.Infrastructure.Comms.Email;

/// <summary>
/// The development and test email provider: it records every envelope and answers <see cref="EmailSendStatus.Accepted"/> unless a result was scripted
/// with <see cref="ScriptNext"/> (consumed in order, one per send). It is the default until a relay is configured (D67); nothing leaves the process.
/// </summary>
public sealed class FakeEmailProvider : IEmailProvider
{
    private readonly ConcurrentQueue<EmailEnvelope> _sent = new();
    private readonly ConcurrentQueue<EmailSendResult> _script = new();

    /// <summary>Every envelope handed to the provider, in order (also the ones that were scripted to fail).</summary>
    public IReadOnlyList<EmailEnvelope> Sent => [.. _sent];

    /// <summary>The next sends answer these results, then the provider accepts again.</summary>
    public void ScriptNext(params EmailSendResult[] results)
    {
        foreach (var result in results)
        {
            _script.Enqueue(result);
        }
    }

    public Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken cancellationToken)
    {
        _sent.Enqueue(envelope);
        return Task.FromResult(_script.TryDequeue(out var next) ? next : EmailSendResult.Accepted("fake-" + Guid.NewGuid().ToString("N")));
    }
}
