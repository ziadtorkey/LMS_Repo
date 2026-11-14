using System.Collections.Concurrent;
using Lms.Domain.Comms;

namespace Lms.Infrastructure.Comms.Push;

/// <summary>The development and test push provider: records every send and answers with scripted results (first in first out; accepted when none is queued). Nothing leaves the process.</summary>
public sealed class FakePushProvider : IPushProvider
{
    private readonly ConcurrentQueue<PushResult> _results = new();
    private readonly ConcurrentQueue<(PushTarget Target, byte[] Payload, TimeSpan TimeToLive, bool Urgent)> _sent = new();

    public string ProviderName => "fake";

    public string? PublicKey { get; set; } = "BDevelopmentOnlyFakePublicKeyForTestsAndLocalRuns_NotARealVapidKey";

    public IReadOnlyCollection<(PushTarget Target, byte[] Payload, TimeSpan TimeToLive, bool Urgent)> Sent => _sent.ToArray();

    public void ScriptNext(PushResult result) => _results.Enqueue(result);

    public void Reset()
    {
        _results.Clear();
        _sent.Clear();
    }

    public Task<PushResult> SendAsync(PushTarget target, byte[] payload, TimeSpan timeToLive, bool urgent, CancellationToken cancellationToken)
    {
        _sent.Enqueue((target, payload, timeToLive, urgent));
        return Task.FromResult(_results.TryDequeue(out var next) ? next : new PushResult(PushResultKind.Accepted));
    }
}
