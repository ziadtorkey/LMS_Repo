using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The live session routes that are not tied to one class course or one session (M4b Task 1 contract, route table "Live sessions"):
/// <c>upcoming</c> (filtered by relationship and unit scope, never 404) and <c>provider-status</c> (<c>LiveSessions.Manage</c>, no
/// relationship check). The rows of the class course routes and of one session live in <c>LiveSessionsProbeRows</c> (Task 4) and
/// those of recording, participants and join come with Tasks 5 to 7. They are pending until their route is mapped.
/// </summary>
public sealed class LiveSessionStatusProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = ProbeMilestones.M4b });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("live sessions upcoming", HttpMethod.Get, Routes.LiveSessionsUpcoming,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LiveSessionsUpcoming)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
        new("live sessions provider status", HttpMethod.Get, Routes.LiveSessionsProviderStatus,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.LiveSessionsProviderStatus)),
            Expect(Ok, Ok, Ok, Ok, Ok, Ok)),
    ];
}
