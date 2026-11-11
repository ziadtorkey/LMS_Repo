using Lms.Shared;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Audit viewer (Task 18, #97): permission-gated; rows are filtered to the caller's client and subtree, so an unknown id
/// (or another client's entry) is a 404 for everyone who may view.
/// </summary>
public sealed class AuditProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("audit list", HttpMethod.Get, Routes.Audit,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Audit)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("audit entry (client A entry under School1)", HttpMethod.Get, Routes.AuditEntryPattern,
            async (w, _) => new ProbeRequest(Routes.AuditEntry(await NewAuditEntryAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("audit export (csv)", HttpMethod.Get, Routes.AuditExport,
            // A fixed window in the past (no rows): the probe checks access, not content. The shared test database
            // gains tens of thousands of audit rows per run, so any open window can hit the 50,000-row export limit
            // (400 export_too_large).
            (_, _) => Task.FromResult(new ProbeRequest(Routes.AuditExport
                + "?format=csv&fromUtc=2000-01-01T00%3A00%3A00Z&toUtc=2000-01-02T00%3A00%3A00Z")),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
    ];

    /// <summary>A fresh client A audit entry stamped at a new unit below School1 (D20: invisible to School2 and client B).</summary>
    private static async Task<Guid> NewAuditEntryAsync(M1ProbeWorld w)
    {
        var unit = await w.NewUnitAsync(w.Callers.A.School1);
        await using var db = w.Callers.Factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        var entry = new Lms.Domain.Auditing.AuditEntry(
            w.Callers.A.Org.Id, Guid.NewGuid(), null, Lms.Domain.Auditing.AuditAction.Updated, "Probe", Guid.NewGuid(),
            null, null, DateTime.UtcNow, null, unit.Path);
        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }
}
