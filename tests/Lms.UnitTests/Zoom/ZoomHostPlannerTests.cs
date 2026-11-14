using Lms.Infrastructure.Zoom;

namespace Lms.UnitTests.Zoom;

public sealed class ZoomHostPlannerTests
{
    private static readonly DateTime T0 = new(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly TimeSpan Buffer = TimeSpan.FromMinutes(10);

    private static HostSlot Slot(string host, Guid org, int startMin, int endMin) => new(host, org, T0.AddMinutes(startMin), T0.AddMinutes(endMin));

    private static HostChoice Pick(string[] pool, HostSlot[] taken, Guid org, int startMin, int endMin, int share = 100) =>
        ZoomHostPlanner.Pick(pool, taken, org, T0.AddMinutes(startMin), T0.AddMinutes(endMin), Buffer, share);

    [Fact]
    public void Picks_the_first_host_of_the_pool_when_nothing_is_booked() =>
        Assert.Equal("h1", Pick(["h1", "h2"], [], A, 0, 60).HostId);

    [Fact]
    public void A_booked_host_is_skipped_and_the_next_one_is_used() =>
        Assert.Equal("h2", Pick(["h1", "h2"], [Slot("h1", B, 0, 60)], A, 10, 50).HostId);

    [Fact]
    public void The_buffer_applies_before_the_start_and_after_the_end()
    {
        // h1 is busy until minute 60; a session starting at 65 is inside the 10-minute buffer, one at 70 is not.
        Assert.Equal(ZoomHostFailure.PoolExhausted, Pick(["h1"], [Slot("h1", B, 0, 60)], A, 65, 100).Failure);
        Assert.Equal("h1", Pick(["h1"], [Slot("h1", B, 0, 60)], A, 70, 100).HostId);
        // and the other way round: the new session must end 10 minutes before the booked one starts
        Assert.Equal(ZoomHostFailure.PoolExhausted, Pick(["h1"], [Slot("h1", B, 100, 160)], A, 40, 95).Failure);
        Assert.Equal("h1", Pick(["h1"], [Slot("h1", B, 100, 160)], A, 40, 90).HostId);
    }

    [Fact]
    public void Exhausted_pool_is_reported_not_double_booked()
    {
        var choice = Pick(["h1", "h2"], [Slot("h1", B, 0, 60), Slot("h2", B, 0, 60)], A, 0, 60);
        Assert.Null(choice.HostId);
        Assert.Equal(ZoomHostFailure.PoolExhausted, choice.Failure);
    }

    [Fact]
    public void An_empty_pool_is_exhausted() =>
        Assert.Equal(ZoomHostFailure.PoolExhausted, Pick([], [], A, 0, 60).Failure);

    [Fact]
    public void A_client_cannot_hold_more_than_its_share_of_the_hosts_in_an_overlapping_slot()
    {
        string[] pool = ["h1", "h2", "h3", "h4", "h5", "h6", "h7", "h8"]; // 25 % of 8 = 2 hosts
        HostSlot[] taken = [Slot("h1", A, 0, 60), Slot("h2", A, 10, 70)];

        var choice = Pick(pool, taken, A, 20, 50, share: 25);

        Assert.Null(choice.HostId);
        Assert.Equal(ZoomHostFailure.ClientShareReached, choice.Failure);
    }

    [Fact]
    public void The_share_counts_only_that_clients_slots_and_only_overlapping_ones()
    {
        string[] pool = ["h1", "h2", "h3", "h4", "h5", "h6", "h7", "h8"];
        HostSlot[] taken =
        [
            Slot("h1", A, 0, 60), Slot("h2", B, 0, 60), Slot("h3", B, 0, 60), // other clients' slots do not count against A
            Slot("h4", A, 300, 360),                                          // A's slot far away does not overlap, h4 is free now
        ];

        Assert.Equal("h4", Pick(pool, taken, A, 20, 50, share: 25).HostId);
    }

    [Fact]
    public void A_tiny_pool_still_gives_every_client_at_least_one_host()
    {
        Assert.Equal("h1", Pick(["h1", "h2"], [], A, 0, 60, share: 10).HostId);
        Assert.Equal(ZoomHostFailure.ClientShareReached, Pick(["h1", "h2"], [Slot("h1", A, 0, 60)], A, 0, 60, share: 10).Failure);
    }

    [Fact]
    public void The_same_host_booked_twice_by_one_client_counts_once()
    {
        string[] pool = ["h1", "h2", "h3", "h4"]; // 50 % = 2 hosts
        var choice = Pick(pool, [Slot("h1", A, 0, 30), Slot("h1", A, 45, 60)], A, 20, 50, share: 50);
        Assert.Equal("h2", choice.HostId);
    }
}
