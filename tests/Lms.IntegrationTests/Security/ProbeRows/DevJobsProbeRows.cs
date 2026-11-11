using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Manual job trigger (M2 Task 4 / Task 19, for E2E): <c>POST /api/dev/jobs/{jobId}/run?asOf=</c>, mapped only when
/// <c>Jobs:EnableManualTrigger=true</c> and gated by <c>Clients.Update</c>, so only a platform admin passes; every
/// client role is 403. The probe runs <c>imports-cleanup</c>, which only removes uncommitted batches older than 7 days.
/// </summary>
public sealed class DevJobsProbeRows : IProbeRowModule
{
    /// <summary>Not in <c>Routes</c> (a development-only route); kept literal and matched against the mapped pattern.</summary>
    internal const string RunPattern = "/api/dev/jobs/{jobId}/run";

    public IEnumerable<ProbeRow> Rows =>
    [
        new("dev jobs run (imports-cleanup)", HttpMethod.Post, RunPattern,
            (_, _) => Task.FromResult(new ProbeRequest($"/api/dev/jobs/imports-cleanup/run?asOf={DateTime.UtcNow:yyyy-MM-dd}")),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok))
        {
            M2Expected = M2(Forbidden, Forbidden, Forbidden, Forbidden),
        },
    ];
}
