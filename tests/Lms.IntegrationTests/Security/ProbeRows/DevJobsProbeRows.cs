using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Manual job trigger (M2 Task 19, #199, for E2E): <c>POST /api/dev/jobs/{jobId}/run?asOf=</c>, mapped only in
/// Development and gated by <c>Clients.Update</c>, so only a platform admin passes; every client role is 403. The probe
/// runs <c>staff-expiry</c> for a day long ago, so it changes nothing for the other tests' data.
/// </summary>
public sealed class DevJobsProbeRows : IProbeRowModule
{
    /// <summary>Not in <c>Routes</c> (a development-only route); kept literal and matched against the mapped pattern.</summary>
    internal const string RunPattern = "/api/dev/jobs/{jobId}/run";

    public IEnumerable<ProbeRow> Rows =>
    [
        new("dev jobs run (staff-expiry)", HttpMethod.Post, RunPattern,
            (_, _) => Task.FromResult(new ProbeRequest("/api/dev/jobs/staff-expiry/run?asOf=2000-01-01")),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok)),
    ];
}
