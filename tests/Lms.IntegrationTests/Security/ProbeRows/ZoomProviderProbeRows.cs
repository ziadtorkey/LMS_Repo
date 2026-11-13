using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The Zoom provider status (M4b Task 5). Platform-level and not tied to a unit: any caller holding LiveSessions.Manage anywhere
/// gets the two flags (no configuration detail), everyone else is refused. Expected statuses per caller are filled in from the
/// first run and checked against the role defaults.
/// </summary>
public sealed class ZoomProviderProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("live sessions provider status", HttpMethod.Get, Routes.LiveSessionsProviderStatus,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LiveSessionsProviderStatus)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
    ];
}
