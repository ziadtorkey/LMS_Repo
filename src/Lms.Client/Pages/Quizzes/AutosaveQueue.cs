using Lms.Client.Services.Api;
using Lms.Shared;
using Lms.Shared.Quizzes;

namespace Lms.Client.Pages.Quizzes;

public enum SaveState { Idle, Saving, Saved, Failed }

/// <summary>
/// Debounced, ordered autosave of quiz answers. Every change gets a sequence number (the server keeps the highest, so late
/// arrivals cannot overwrite newer answers). Only the latest answer per question is sent. A network failure keeps the
/// answers queued and shows <see cref="SaveState.Failed"/> until <see cref="RetryAsync"/> or the next change succeeds;
/// <c>attempt_expired</c> is reported through <see cref="Expired"/>.
/// </summary>
public sealed class AutosaveQueue(
    Func<SaveAnswerRequest, CancellationToken, Task<SaveAnswerResultDto>> save, TimeProvider clock, TimeSpan debounce)
{
    private readonly Dictionary<Guid, SaveAnswerRequest> _pending = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _timer;
    private long _sequence;

    public SaveState State { get; private set; } = SaveState.Idle;
    public bool HasPending => _pending.Count > 0;
    public event Action? Changed;
    public event Action<SaveAnswerResultDto>? Saved;
    public event Action? Expired;

    /// <summary>Continue numbering after the answers already stored on the server (resumed attempts).</summary>
    public void StartAfter(long storedSequence) => _sequence = Math.Max(_sequence, storedSequence);

    public void Enqueue(Guid questionId, IReadOnlyList<Guid> selected, string? text)
    {
        _pending[questionId] = new SaveAnswerRequest(questionId, selected, text, ++_sequence);
        _timer?.Cancel();
        _timer = new CancellationTokenSource();
        _ = RunAfterDelayAsync(_timer.Token);
    }

    private async Task RunAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(debounce, clock, token);
            await FlushAsync();
        }
        catch (OperationCanceledException)
        {
            // a newer change restarted the debounce
        }
    }

    /// <summary>Sends everything pending now (before submitting, or on retry).</summary>
    public async Task FlushAsync()
    {
        var expired = false;
        await _gate.WaitAsync();
        try
        {
            while (_pending.Count > 0)
            {
                var request = _pending.Values.OrderBy(r => r.Sequence).First();
                Set(SaveState.Saving);
                try
                {
                    var result = await save(request, CancellationToken.None);
                    // A newer answer for the same question may have been queued meanwhile: keep it.
                    if (_pending.TryGetValue(request.QuestionId, out var current) && current.Sequence == request.Sequence) _pending.Remove(request.QuestionId);
                    Saved?.Invoke(result);
                }
                catch (ApiProblemException ex) when (ex.Code == ProblemCodes.attempt_expired)
                {
                    _pending.Clear();
                    Set(SaveState.Failed);
                    expired = true;
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or ApiProblemException)
                {
                    Set(SaveState.Failed);
                    return;
                }
            }
            if (!expired) Set(SaveState.Saved);
        }
        finally
        {
            _gate.Release();
        }
        // Raised after the gate is free: the handler submits the attempt, which flushes again.
        if (expired) Expired?.Invoke();
    }

    public Task RetryAsync() => FlushAsync();

    private void Set(SaveState state)
    {
        State = state;
        Changed?.Invoke();
    }
}
