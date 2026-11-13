using System.Collections.Concurrent;
using Lms.Domain.LiveSessions;

namespace Lms.Infrastructure.LiveSessions;

/// <summary>
/// An in-memory <see cref="IMeetingProvider"/> for tests and for a host where Zoom is not configured. It records every call,
/// hands out fake links and can be told to fail, so authoring is testable without a network. It has no host pool, so it never
/// calls <see cref="MeetingRequest.ReserveHostAsync"/>.
/// </summary>
public sealed class FakeMeetingProvider : IMeetingProvider
{
    private readonly ConcurrentDictionary<string, MeetingRequest> _meetings = new();
    private readonly ConcurrentQueue<MeetingFailure?> _script = new();
    private int _failures;
    private int _failureKind;
    private int _next;

    public string Name => "Fake";

    /// <summary>The requests of every meeting that exists (created and not deleted), by external id.</summary>
    public IReadOnlyDictionary<string, MeetingRequest> Meetings => _meetings;

    public ConcurrentQueue<string> Deleted { get; } = new();

    public ConcurrentQueue<string> Updated { get; } = new();

    /// <summary>The next <paramref name="count"/> create, update and delete calls throw <see cref="MeetingProviderException"/>.</summary>
    public void FailNext(int count, MeetingFailure failure = MeetingFailure.Failed)
    {
        Interlocked.Exchange(ref _failureKind, (int)failure);
        Interlocked.Exchange(ref _failures, count);
    }

    /// <summary>
    /// Scripts the next create calls: <c>null</c> = the call succeeds, a kind = it throws that failure; once the script is used up
    /// calls succeed again. Lets a test fail a later occurrence of a series after earlier ones were created.
    /// </summary>
    public void ScriptCreates(params MeetingFailure?[] outcomes)
    {
        _script.Clear();
        foreach (var outcome in outcomes)
        {
            _script.Enqueue(outcome);
        }
    }

    /// <summary>The next call throws this exception as it is (what a timeout or a network error of a real provider looks like).</summary>
    public Exception? NextException { get; set; }

    public void Reset()
    {
        _meetings.Clear();
        Deleted.Clear();
        Updated.Clear();
        _script.Clear();
        NextException = null;
        Interlocked.Exchange(ref _failures, 0);
    }

    public Task<MeetingResult> CreateMeetingAsync(MeetingRequest request, CancellationToken ct = default)
    {
        if (_script.TryDequeue(out var scripted) && scripted is { } kind)
        {
            throw new MeetingProviderException(kind, "The fake provider was told to fail.", 30);
        }

        ThrowIfFailing();
        var id = "fake-" + Interlocked.Increment(ref _next) + "-" + Guid.NewGuid().ToString("N")[..8];
        _meetings[id] = request;
        return Task.FromResult(new MeetingResult(id, $"https://fake.zoom.test/j/{id}?pwd=join", $"https://fake.zoom.test/s/{id}?zak=host", "fake-host"));
    }

    public Task UpdateMeetingAsync(string externalMeetingId, MeetingRequest request, CancellationToken ct = default)
    {
        ThrowIfFailing();
        _meetings[externalMeetingId] = request;
        Updated.Enqueue(externalMeetingId);
        return Task.CompletedTask;
    }

    public Task DeleteMeetingAsync(string externalMeetingId, CancellationToken ct = default)
    {
        ThrowIfFailing();
        _meetings.TryRemove(externalMeetingId, out _);
        Deleted.Enqueue(externalMeetingId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MeetingParticipant>> GetParticipantsAsync(string externalMeetingId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MeetingParticipant>>([]);

    public Task<IReadOnlyList<MeetingRecording>> GetRecordingsAsync(string externalMeetingId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MeetingRecording>>([]);

    public Task<ProviderHealth> GetHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderHealth(true, true));

    private void ThrowIfFailing()
    {
        if (NextException is { } unexpected)
        {
            NextException = null;
            throw unexpected;
        }

        if (Interlocked.Decrement(ref _failures) >= 0)
        {
            throw new MeetingProviderException((MeetingFailure)_failureKind, "The fake provider was told to fail.", 30);
        }

        Interlocked.Exchange(ref _failures, 0);
    }
}
