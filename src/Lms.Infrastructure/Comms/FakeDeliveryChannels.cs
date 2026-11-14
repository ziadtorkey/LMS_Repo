using System.Collections.Concurrent;
using Lms.Domain.Comms;
using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Comms;

/// <summary>
/// The development and test channel: records every delivery, answers with programmable outcomes (<see cref="Enqueue"/>, first in first out; accepted
/// when none is queued) and can be slowed with <see cref="Delay"/> for concurrency tests. Nothing leaves the process. A real provider replaces it
/// per channel (the email relay of M6 Task 6, the push provider of Task 7).
/// </summary>
public sealed class FakeDeliveryChannel(string channel) : IDeliveryChannel
{
    private readonly ConcurrentQueue<DeliveryOutcome> _outcomes = new();
    private readonly ConcurrentQueue<DeliveryRequest> _sent = new();

    public string Channel { get; } = channel;

    public string ProviderName => "fake";

    public TimeSpan Delay { get; set; }

    /// <summary>Every request the channel was asked to send, accepted or not.</summary>
    public IReadOnlyCollection<DeliveryRequest> Sent => _sent.ToArray();

    public void Enqueue(DeliveryOutcome outcome) => _outcomes.Enqueue(outcome);

    public void Reset()
    {
        _outcomes.Clear();
        _sent.Clear();
        Delay = TimeSpan.Zero;
    }

    public async Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        _sent.Enqueue(request);
        return _outcomes.TryDequeue(out var next) ? next : DeliveryOutcome.Accepted("fake-" + request.OutboxId.ToString("N"));
    }
}

/// <summary>The two fake channels as one singleton, so a test reaches them by name.</summary>
public sealed class FakeDeliveryChannels
{
    public FakeDeliveryChannel Email { get; } = new(NotificationChannels.Email);

    public FakeDeliveryChannel Push { get; } = new(NotificationChannels.Push);

    public IEnumerable<IDeliveryChannel> All => [Email, Push];

    public void Reset()
    {
        Email.Reset();
        Push.Reset();
    }
}
