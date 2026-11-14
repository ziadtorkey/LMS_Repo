using Lms.Domain.Moderation;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Reporting and moderating messages (M6 Task 15, D70): the queue and every report route are the Org Admin's (<c>Messaging.Moderate</c>): a Teacher is 403, a School 2 admin and the other client reach
/// no report of School 1 (404, zeros in the stats), the School 1 admin and the root admin do. Reporting needs <c>Messaging.Send</c> and a participant: an unknown message is 404 for everyone.
/// </summary>
public sealed class ModerationProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows =>
    [
        new("message report", HttpMethod.Post, Routes.MessageReportPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.MessageReport(Guid.NewGuid()), new ReportMessageRequest(ReportReasons.Spam, null))),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
        new("moderation queue", HttpMethod.Get, Routes.ModerationReports,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ModerationReports)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("moderation report detail", HttpMethod.Get, Routes.ModerationReportPattern,
            async (w, _) => new ProbeRequest(Routes.ModerationReport(await NewReportAsync(w))),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("moderation report resolve", HttpMethod.Post, Routes.ModerationReportResolvePattern,
            async (w, _) => new ProbeRequest(Routes.ModerationReportResolve(await NewReportAsync(w)), new ResolveReportRequest(ModerationActions.Dismiss, null, null)),
            Expect(Ok, Ok, NotFound, Forbidden, NotFound, Ok)),
        new("moderation bulk dismiss", HttpMethod.Post, Routes.ModerationReportsBulkDismiss,
            async (w, _) => new ProbeRequest(Routes.ModerationReportsBulkDismiss, new BulkDismissRequest([await NewReportAsync(w)], null)),
            Expect(NoContent, NoContent, NoContent, Forbidden, NoContent, NoContent)),
        new("moderation report attachment", HttpMethod.Get, Routes.ModerationReportAttachmentPattern,
            async (w, _) => new ProbeRequest(Routes.ModerationReportAttachment(await NewReportAsync(w), Guid.NewGuid())),
            Expect(NotFound, NotFound, NotFound, Forbidden, NotFound, NotFound)),
        new("moderation stats", HttpMethod.Get, Routes.ModerationStats,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ModerationStats)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("moderation restrictions", HttpMethod.Get, Routes.ModerationRestrictions,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ModerationRestrictions)),
            Expect(Ok, Ok, Ok, Forbidden, Ok, Ok)),
        new("moderation lift a restriction", HttpMethod.Delete, Routes.ModerationRestrictionPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.ModerationRestriction(Guid.NewGuid()))),
            Expect(NotFound, NotFound, NotFound, Forbidden, NotFound, NotFound)),
    ];

    /// <summary>A pending report of a message sent by a user of School 1, visible at School 1 and above.</summary>
    private static async Task<Guid> NewReportAsync(M1ProbeWorld w)
    {
        await using var db = w.Callers.Factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var org = w.Callers.A.Org.Id;
        var report = new MessageReport(org, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ReportReasons.Spam, null, "[]", null);
        db.Add(report);
        db.Add(new MessageReportUnit(report.Id, org, w.Callers.A.School1.Id, w.Callers.A.School1.Path));
        await db.SaveChangesAsync();
        return report.Id;
    }
}
