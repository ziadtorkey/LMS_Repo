using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The provider-status route, which is tied to no class course or session (<c>LiveSessions.Manage</c>, no relationship check). The
/// rows of the class course routes, of one session and of <c>upcoming</c> live in <c>LiveSessionsProbeRows</c>; the Zoom webhook row is
/// in <c>ZoomProviderProbeRows</c>. Each route has exactly one row: a duplicate fails the matrix.
/// </summary>
public sealed class LiveSessionStatusProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("live sessions provider status", HttpMethod.Get, Routes.LiveSessionsProviderStatus,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LiveSessionsProviderStatus)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
    ];
}
