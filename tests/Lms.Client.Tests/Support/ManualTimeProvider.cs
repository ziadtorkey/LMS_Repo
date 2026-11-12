namespace Lms.Client.Tests.Support;

/// <summary>A <see cref="TimeProvider"/> the test advances by hand: timers and <c>Task.Delay(…, provider)</c> fire when their time is reached.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_timers) _timers.Add(timer);
        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that falls due on the way (in order).</summary>
    public void Advance(TimeSpan by)
    {
        var target = _now + by;
        while (true)
        {
            ManualTimer? next;
            lock (_timers) next = _timers.Where(t => t.Next is { } n && n <= target).OrderBy(t => t.Next).FirstOrDefault();
            if (next is null) break;
            _now = next.Next!.Value;
            next.Fire();
        }
        _now = target;
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period;
        public DateTimeOffset? Next { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            _period = period;
            Next = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Fire()
        {
            Next = _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan ? owner._now + _period : null;
            callback(state);
        }

        public void Dispose() => Next = null;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
