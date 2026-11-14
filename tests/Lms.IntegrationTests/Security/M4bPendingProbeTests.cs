using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The pins of the M4b probes (see <see cref="M4bPending"/>): a pinned key that has become live fails here until its pin is removed, so the probes that waited
/// for it run; and every M4b probe row whose route is not mapped yet must be pinned, so a row cannot stay pending by accident.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4bPendingProbeTests(LmsApiFactory factory)
{
    public static TheoryData<string> PinKeys()
    {
        var data = new TheoryData<string>();
        foreach (var key in M4bPending.Pins.Keys)
        {
            data.Add(key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PinKeys))]
    public void Every_pin_is_still_pending(string key) =>
        Assert.False(M4bPending.IsLive(factory, key),
            $"{key} is live now ({M4bPending.Pins[key]} is on this branch). Remove it from M4bPending.Pins in this PR so the probes that wait for it run, and write the probe bodies that fail with instructions.");

    [Fact]
    public void Every_M4b_probe_row_that_is_not_mapped_is_pinned()
    {
        var unpinned = M1ProbeRows.All
            .Where(r => M4bRoutes.Is(r.Pattern) && !M4bPending.IsLive(factory, $"ROUTE {r.Method.Method} {r.Pattern}"))
            .Select(r => $"ROUTE {r.Method.Method} {r.Pattern}")
            .Where(key => !M4bPending.Pins.ContainsKey(key))
            .ToList();
        Assert.True(unpinned.Count == 0, "M4b probe rows whose route is not mapped and that are not pinned in M4bPending.Pins:\n" + string.Join('\n', unpinned));
    }
}
