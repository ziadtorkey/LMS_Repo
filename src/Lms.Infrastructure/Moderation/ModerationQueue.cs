using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Moderation;

/// <summary>
/// The moderation reads (M6 Task 15, FR-MSG-04, D70, L6, L16; plan site 6). A report is visible to an Org Admin assigned at or above <b>any</b> of its
/// <see cref="MessageReportUnit"/> rows, through the ordinary unit-subtree filter of that table: nothing here bypasses a filter to widen the scope. The moderator reads the reported message
/// with its frozen context and nothing else of the conversation, and <b>every read of a detail or an attachment is audited</b> as <c>ModerationRead</c> (who, which report, which kind) without
/// any text. A report outside the scope does not exist (404); the counts outside it are zero.
/// </summary>
public sealed class ModerationQueue(LmsDbContext db, ICurrentContext context, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The reports the caller may see: those with at least one unit row in the caller's scope.</summary>
    public IQueryable<MessageReport> Visible() =>
        db.Set<MessageReport>().Where(r => db.Set<MessageReportUnit>().Any(u => u.ReportId == r.Id));

    public async Task<(IReadOnlyList<MessageReportListItemDto> Items, int Total)> ListAsync(MessageReportQuery query, CancellationToken ct)
    {
        var rows = Visible();
        rows = query.Status is { } status ? rows.Where(r => r.Status == status) : rows.Where(r => r.Status == MessageReport.OpenStatus || r.Status == MessageReport.InReviewStatus);
        if (query.Reason is { } reason)
        {
            rows = rows.Where(r => r.Reason == reason);
        }

        var total = await rows.CountAsync(ct);
        var size = Math.Clamp(query.PageSize, 1, Paging.MaxPageSize);
        // A safety concern first (its reason sorts first), then the oldest.
        var page = await rows.OrderBy(r => r.Reason == ReportReasons.SafetyConcern ? 0 : 1).ThenBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Skip((Math.Max(query.Page, 1) - 1) * size).Take(size).ToListAsync(ct);
        var now = clock.UtcNow;
        var users = await NamesAsync(page.Select(r => r.ReportedUserId), ct);
        return (page.Select(r =>
        {
            users.TryGetValue(r.ReportedUserId, out var user);
            var reported = Context(r).FirstOrDefault(c => c.IsReported);
            return new MessageReportListItemDto(
                r.Id, r.Reason, r.Status, user.Ar, user.En, Snippet(reported?.Body), r.CreatedAt, Math.Max(0, (int)(now - r.CreatedAt).TotalHours), r.Reason == ReportReasons.SafetyConcern);
        }).ToList(), total);
    }

    /// <summary>The report as the moderator sees it. Opening it moves an Open report to InReview and is audited; the reporter is never part of it.</summary>
    public async Task<MessageReportDto?> OpenAsync(Guid id, CancellationToken ct)
    {
        var report = await Visible().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (report is null)
        {
            return null;
        }

        report.MarkInReview();
        WriteRead(report, "Detail");
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(report, ct);
    }

    public async Task<MessageReportDto> ToDtoAsync(MessageReport report, CancellationToken ct)
    {
        var context = Context(report).Select(c => new ReportContextMessageDto(c.MessageId, c.SenderNameAr, c.SenderNameEn, c.Body, c.AttachmentNames, c.CreatedAtUtc, c.IsReported)).ToList();
        return new MessageReportDto(
            report.Id, report.MessageId, report.ConversationId, report.Reason, report.Note, report.Status, context, await SenderSummaryAsync(report, ct),
            report.ReviewDueUtc, report.Action, null, report.ResolvedAtUtc, clock.UtcNow);
    }

    /// <summary>Who sent the reported message in numbers: their role, and how many times this client has had them reported or had a message of theirs removed.</summary>
    private async Task<SenderSummaryDto> SenderSummaryAsync(MessageReport report, CancellationToken ct)
    {
        var names = await NamesAsync([report.ReportedUserId], ct);
        names.TryGetValue(report.ReportedUserId, out var user);
        var prior = db.Set<MessageReport>().Where(r => r.OrganizationId == report.OrganizationId && r.ReportedUserId == report.ReportedUserId && r.Id != report.Id);
        var priorReports = await prior.CountAsync(ct);
        var priorRemovals = await prior.CountAsync(r => r.Action == ModerationActions.Remove || r.Action == ModerationActions.RemoveAndRestrict, ct);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        // ISOLATION-BYPASS: the sender holds their roles outside the moderator scope; client matched, live rows, one user, valid today; only the role ids are used to label the sender.
        var roleIds = await db.Set<RoleAssignment>().IgnoreQueryFilters()
            .Where(a => !a.IsDeleted && a.OrganizationId == report.OrganizationId && a.UserId == report.ReportedUserId && (a.ValidFrom == null || a.ValidFrom <= today) && (a.ValidTo == null || a.ValidTo >= today))
            .Select(a => a.RoleId).ToListAsync(ct);
        // ISOLATION-BYPASS: the role rows of those assignments (built-in roles of the client); only a built-in role key leaves, to label the sender.
        var roleKey = await db.Set<Role>().IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.OrganizationId == report.OrganizationId && roleIds.Contains(r.Id) && r.BuiltInKey != null).Select(r => r.BuiltInKey).FirstOrDefaultAsync(ct);
        return new SenderSummaryDto(user.Ar, user.En, roleKey ?? string.Empty, null, null, priorReports, priorRemovals);
    }

    /// <summary>Writes the audit row of a moderator read (no text, no names): the report, the kind of read and the moderator.</summary>
    public void WriteRead(MessageReport report, string kind, Guid? fileId = null) =>
        db.Add(new AuditEntry(
            report.OrganizationId, context.UserId, context.AssignmentId, AuditAction.Created, "ModerationRead", report.Id, null,
            JsonSerializer.Serialize(new { kind, messageId = report.MessageId, fileId }, Json), clock.UtcNow, null, context.ScopePath));

    public async Task<ModerationStatsDto> StatsAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var day = now.AddHours(-24);
        var month = now.AddDays(-30);
        var visible = Visible();
        var open = await visible.CountAsync(r => r.Status == MessageReport.OpenStatus || r.Status == MessageReport.InReviewStatus, ct);
        var fresh = await visible.CountAsync(r => r.CreatedAt >= day, ct);
        var reported = visible.Select(r => r.ReportedUserId);
        var restricted = await db.Set<MessagingRestriction>().CountAsync(x => reported.Contains(x.UserId) && (x.UntilUtc == null || x.UntilUtc > now), ct);
        var decided = await visible.Where(r => r.ResolvedAtUtc != null && r.ResolvedAtUtc >= month).Select(r => new { r.CreatedAt, Resolved = r.ResolvedAtUtc!.Value }).ToListAsync(ct);
        var hours = decided.Select(d => (d.Resolved - d.CreatedAt).TotalHours).Order().ToList();
        var median = hours.Count == 0 ? 0 : hours.Count % 2 == 1 ? hours[hours.Count / 2] : (hours[hours.Count / 2 - 1] + hours[hours.Count / 2]) / 2;
        return new ModerationStatsDto(open, fresh, restricted, Math.Round(median, 1));
    }

    public async Task<IReadOnlyList<RestrictionDto>> RestrictionsAsync(int page, int pageSize, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var reported = Visible().Select(r => r.ReportedUserId);
        var size = Math.Clamp(pageSize, 1, Paging.MaxPageSize);
        var rows = await db.Set<MessagingRestriction>().Where(x => reported.Contains(x.UserId) && x.UntilUtc != null && x.UntilUtc > now)
            .OrderBy(x => x.UntilUtc).ThenBy(x => x.Id).Skip((Math.Max(page, 1) - 1) * size).Take(size).ToListAsync(ct);
        var users = await NamesAsync(rows.Select(x => x.UserId), ct);
        return rows.Select(x =>
        {
            users.TryGetValue(x.UserId, out var user);
            return new RestrictionDto(x.UserId, user.Ar, user.En, x.UntilUtc!.Value, null, x.CreatedAt);
        }).ToList();
    }

    private async Task<Dictionary<Guid, (string Ar, string En)>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return (await db.Users.Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => (u.DisplayNameAr, u.DisplayNameEn));
    }

    private static List<ReportContextItem> Context(MessageReport report) =>
        JsonSerializer.Deserialize<List<ReportContextItem>>(report.ContextJson, Json) ?? [];

    private static string Snippet(string? body) => string.IsNullOrEmpty(body) ? string.Empty : body.Length <= 120 ? body : body[..120] + "…";
}
