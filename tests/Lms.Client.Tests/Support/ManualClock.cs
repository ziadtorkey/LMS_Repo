namespace Lms.Client.Tests.Support;

/// <summary>A clock the test moves by hand; timers created on it fire only when the test advances past their due time.</summary>
public sealed class ManualClock : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, _now + dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves time forward in steps and fires every timer that comes due on the way (a periodic one as often as it is due).</summary>
    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_timers) next = _timers.Where(t => !t.Disposed && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
            if (next is null) break;
            _now = next.Due > _now ? next.Due : _now;
            next.Fire();
        }
        _now = target;
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, DateTimeOffset due, TimeSpan period) : ITimer
    {
        public DateTimeOffset Due { get; private set; } = due;
        public bool Disposed { get; private set; }

        public void Fire()
        {
            if (period > TimeSpan.Zero && period != Timeout.InfiniteTimeSpan) Due += period;
            else Disposed = true;
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan newPeriod) => true;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
