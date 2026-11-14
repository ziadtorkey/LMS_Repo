using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Zoom;
using Microsoft.Extensions.Options;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomHostAllocatorTests
{
    private static readonly DateTime T0 = new(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The slot table in memory; reading it hands out copies, like a query would.</summary>
    private sealed class MemorySlots : IHostSlotStore
    {
        private readonly List<HostSlot> _slots = [];

        public int Reads;

        public Task<IReadOnlyList<HostSlot>> SlotsAroundAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            Interlocked.Increment(ref Reads);
            lock (_slots)
            {
                return Task.FromResult<IReadOnlyList<HostSlot>>([.. _slots.Where(s => s.StartUtc < toUtc && s.EndUtc > fromUtc)]);
            }
        }

        public void Add(HostSlot slot)
        {
            lock (_slots)
            {
                _slots.Add(slot);
            }
        }

        public int Count
        {
            get
            {
                lock (_slots)
                {
                    return _slots.Count;
                }
            }
        }
    }

    private static ZoomHostAllocator Build(MemorySlots slots, int hosts, int share = 100, IJobKeyLock? keyLock = null, TimeSpan? lockTimeout = null) =>
        new(Options.Create(new ZoomOptions { HostPool = [.. Enumerable.Range(1, hosts).Select(i => "h" + i)], HostBufferMinutes = 10 }),
            keyLock ?? new InProcessJobKeyLock(), slots, () => share, lockTimeout);

    [Fact]
    public async Task The_chosen_host_is_reserved_before_the_lock_is_released()
    {
        var slots = new MemorySlots();
        var allocator = Build(slots, 2);
        var org = Guid.NewGuid();

        var first = await allocator.AllocateAsync(org, T0, T0.AddMinutes(60), host =>
        {
            slots.Add(new HostSlot(host, org, T0, T0.AddMinutes(60)));
            return Task.CompletedTask;
        });
        var second = await allocator.AllocateAsync(org, T0, T0.AddMinutes(60), host =>
        {
            slots.Add(new HostSlot(host, org, T0, T0.AddMinutes(60)));
            return Task.CompletedTask;
        });

        Assert.Equal("h1", first.HostId);
        Assert.Equal("h2", second.HostId);
    }

    [Fact]
    public async Task Host_pool_never_double_books_a_slot_under_concurrency()
    {
        var slots = new MemorySlots();
        var allocator = Build(slots, hosts: 3);
        var org = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
            allocator.AllocateAsync(org, T0, T0.AddMinutes(60), host =>
            {
                Thread.Sleep(5); // a slow insert: without the lock two callers would read the same free host
                slots.Add(new HostSlot(host, org, T0, T0.AddMinutes(60)));
                return Task.CompletedTask;
            }))));

        var booked = results.Where(r => r.HostId is not null).Select(r => r.HostId).ToList();
        Assert.Equal(3, booked.Count);
        Assert.Equal(3, booked.Distinct().Count());
        Assert.All(results.Where(r => r.HostId is null), r => Assert.Equal(ZoomHostFailure.PoolExhausted, r.Failure));
        Assert.Equal(3, slots.Count);
    }

    [Fact]
    public async Task Exhausted_pool_answers_without_calling_reserve()
    {
        var slots = new MemorySlots();
        slots.Add(new HostSlot("h1", Guid.NewGuid(), T0, T0.AddMinutes(60)));
        var reserved = false;

        var choice = await Build(slots, 1).AllocateAsync(Guid.NewGuid(), T0, T0.AddMinutes(30), _ =>
        {
            reserved = true;
            return Task.CompletedTask;
        });

        Assert.Equal(ZoomHostFailure.PoolExhausted, choice.Failure);
        Assert.False(reserved);
    }

    [Fact]
    public async Task Client_cannot_hold_more_than_its_host_share()
    {
        var slots = new MemorySlots();
        var allocator = Build(slots, hosts: 4, share: 25); // one host per client
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Task Reserve(Guid org, string host)
        {
            slots.Add(new HostSlot(host, org, T0, T0.AddMinutes(60)));
            return Task.CompletedTask;
        }

        Assert.Equal("h1", (await allocator.AllocateAsync(a, T0, T0.AddMinutes(60), h => Reserve(a, h))).HostId);
        var second = await allocator.AllocateAsync(a, T0, T0.AddMinutes(60), h => Reserve(a, h));
        var other = await allocator.AllocateAsync(b, T0, T0.AddMinutes(60), h => Reserve(b, h));

        Assert.Equal(ZoomHostFailure.ClientShareReached, second.Failure);
        Assert.Equal("h2", other.HostId);
    }

    [Fact]
    public async Task The_allocator_asks_the_store_only_for_the_window_around_the_session()
    {
        var slots = new MemorySlots();
        await Build(slots, 1).AllocateAsync(Guid.NewGuid(), T0, T0.AddMinutes(60), _ => Task.CompletedTask);

        Assert.Equal(1, slots.Reads);
    }

    [Fact]
    public async Task A_caller_that_cannot_get_the_lock_in_time_gets_provider_unavailable_with_a_retry_hint()
    {
        var slots = new MemorySlots();
        var keyLock = new InProcessJobKeyLock();
        using var held = keyLock.Acquire("zoom:host-pool", TimeSpan.FromSeconds(1)); // someone else is allocating
        var allocator = Build(slots, 1, keyLock: keyLock, lockTimeout: TimeSpan.FromMilliseconds(100));

        var ex = await Assert.ThrowsAsync<ZoomProviderException>(() =>
            allocator.AllocateAsync(Guid.NewGuid(), T0, T0.AddMinutes(60), _ => Task.CompletedTask));

        Assert.Equal(ZoomFailure.Unavailable, ex.Failure);
        Assert.NotNull(ex.RetryAfter);
        Assert.Equal(0, slots.Reads); // it never read or reserved anything
    }

    [Fact]
    public async Task Duplicate_or_blank_hosts_in_the_pool_count_once()
    {
        var slots = new MemorySlots();
        var options = Options.Create(new ZoomOptions { HostPool = ["h1", "H1 ", "", "  ", "h2"], HostBufferMinutes = 10 });
        var allocator = new ZoomHostAllocator(options, new InProcessJobKeyLock(), slots, () => 100);
        var org = Guid.NewGuid();

        Task Reserve(string host)
        {
            slots.Add(new HostSlot(host, org, T0, T0.AddMinutes(60)));
            return Task.CompletedTask;
        }

        var first = await allocator.AllocateAsync(org, T0, T0.AddMinutes(60), Reserve);
        var second = await allocator.AllocateAsync(org, T0, T0.AddMinutes(60), Reserve);
        var third = await allocator.AllocateAsync(org, T0, T0.AddMinutes(60), Reserve);

        Assert.Equal("h1", first.HostId);
        Assert.Equal("h2", second.HostId);
        Assert.Equal(ZoomHostFailure.PoolExhausted, third.Failure); // two real hosts, not four
    }

    [Fact]
    public async Task A_failing_reserve_releases_the_lock_for_the_next_caller()
    {
        var slots = new MemorySlots();
        var allocator = Build(slots, 1);
        var org = Guid.NewGuid();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            allocator.AllocateAsync(org, T0, T0.AddMinutes(60), _ => throw new InvalidOperationException("insert failed")));

        var choice = await allocator.AllocateAsync(org, T0, T0.AddMinutes(60), _ => Task.CompletedTask);
        Assert.Equal("h1", choice.HostId);
    }
}
