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
        // The one anonymous M4b route: every caller, signed in or not, is an unsigned request to Zoom's webhook and is refused the same way.
        new("zoom webhook (unsigned)", HttpMethod.Post, Routes.ZoomWebhook,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ZoomWebhook, new { @event = "meeting.ended" })),
            Expect(Unauthorized, Unauthorized, Unauthorized, Unauthorized, Unauthorized, Unauthorized)),
    ];
}
