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
            // A narrow window: the shared test database accumulates audit rows, and an unfiltered platform-wide
            // export would hit the 50,000-row export limit (400 export_too_large) as the suite grows.
            (_, _) => Task.FromResult(new ProbeRequest(Routes.AuditExport + "?format=csv&fromUtc="
                + Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(-5).ToString("O")))),
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
