using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Moderation;

public enum ReportOutcome
{
    Created,
    NotFound,
    OwnMessage,
    Invalid,
    Duplicate,
    RateLimited,
}

public sealed record ReportResult(ReportOutcome Outcome, Guid? ReportId = null);

/// <summary>One message of the frozen context, stored in the report as JSON (names and text at the time of the report).</summary>
public sealed record ReportContextItem(Guid MessageId, string SenderNameAr, string SenderNameEn, string? Body, List<string> AttachmentNames, DateTime CreatedAtUtc, bool IsReported);

/// <summary>
/// Reporting a message (M6 Task 15, FR-MSG-04, D66, L16). Only a participant who is not the sender may report (a miss is 404, so nothing tells whether a message exists), once per
/// message, at most <c>Moderation.MaxReportsPerUserPerDay</c> a day. The report <b>freezes</b> the reported message with five messages before and after (text and attachment names),
/// and the units at which a moderator may see it (L16): the conversation's context unit (a class or course group), else every active unit of the sender, else the client root.
/// The moderators (Org Admins at or above those units) are told with the reason code only; a safety concern is mandatory and urgent.
/// </summary>
public sealed class ReportService(
    LmsDbContext db, ICurrentContext context, IClock clock, ISettingsResolver settings, IOrgTree orgTree, INotificationPublisher publisher, IConfiguration configuration,
    ILogger<ReportService> logger)
{
    /// <summary>The context the report rows are written in: the client root, no user. A report is placed at units outside the reporter's own scope (the sender's units, the root), and a write there is only allowed from a context that covers them.</summary>
    private sealed class ReportWriterContext(Guid organizationId, string rootPath) : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => organizationId;
        public Guid? AssignmentId => null;
        public string? ScopePath => rootPath;
        public bool IsPlatformAdmin => false;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReportResult> CreateAsync(Guid messageId, string? reason, string? note, CancellationToken ct)
    {
        if (context.UserId is not { } me || context.OrganizationId is not { } organizationId)
        {
            return new ReportResult(ReportOutcome.NotFound);
        }

        var message = await db.Set<Message>().FirstOrDefaultAsync(m => m.Id == messageId, ct);
        var participant = message is null
            ? null
            : await db.Set<ConversationParticipant>().FirstOrDefaultAsync(p => p.ConversationId == message.ConversationId && p.UserId == me, ct);
        if (message is null || participant is null || message.State == Message.DeletedState || participant.LeftAtUtc is { } left && message.SentAtUtc > left)
        {
            return new ReportResult(ReportOutcome.NotFound);
        }

        if (message.SenderUserId == me)
        {
            return new ReportResult(ReportOutcome.OwnMessage);
        }

        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reason is null || !ReportReasons.All.Contains(reason) || cleanNote?.Length > MessagingLimits.ReportNoteMaxLength)
        {
            return new ReportResult(ReportOutcome.Invalid);
        }

        if (await db.Set<MessageReport>().AnyAsync(r => r.ReporterUserId == me && r.MessageId == messageId, ct))
        {
            return new ReportResult(ReportOutcome.Duplicate);
        }

        var now = clock.UtcNow;
        var path = context.ScopePath ?? string.Empty;
        var cap = (await settings.ResolveAsync(SettingKeys.ModerationMaxReportsPerUserPerDay, path, ct)).Value;
        var since = now.AddHours(-24);
        if (await db.Set<MessageReport>().CountAsync(r => r.ReporterUserId == me && r.CreatedAt >= since, ct) >= cap)
        {
            return new ReportResult(ReportOutcome.RateLimited);
        }

        var conversation = await db.Set<Conversation>().FirstAsync(c => c.Id == message.ConversationId, ct);
        var frozen = await FreezeContextAsync(message, ct);
        DateTime? due = reason == ReportReasons.SafetyConcern
            ? now.AddHours((await settings.ResolveAsync(SettingKeys.ModerationSafetyReviewHours, path, ct)).Value)
            : null;
        var report = new MessageReport(organizationId, message.Id, message.ConversationId, me, message.SenderUserId, reason, cleanNote, JsonSerializer.Serialize(frozen, Json), due);
        var units = await UnitsOfAsync(organizationId, message.SenderUserId, conversation, ct);
        var root = await orgTree.GetRootAsync(organizationId, ct);
        await using var writer = LmsDbContextFactory.Create(configuration.GetConnectionString("Lms")!, new ReportWriterContext(organizationId, root.Path), clock);
        writer.Add(report);
        writer.AddRange(units.Select(u => new MessageReportUnit(report.Id, organizationId, u.Id, u.Path)));
        try
        {
            await writer.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
        {
            return new ReportResult(ReportOutcome.Duplicate); // the same report was filed by a second request at once
        }

        await NotifyModeratorsAsync(report, units, ct);
        return new ReportResult(ReportOutcome.Created, report.Id);
    }

    /// <summary>The reported message and the five messages before and after it, as text and attachment names (a deleted or removed message shows no text).</summary>
    private async Task<List<ReportContextItem>> FreezeContextAsync(Message reported, CancellationToken ct)
    {
        var all = await db.Set<Message>().Where(m => m.ConversationId == reported.ConversationId)
            .OrderBy(m => m.SentAtUtc).ThenBy(m => m.Id).Select(m => new { m.Id, m.SentAtUtc }).ToListAsync(ct);
        var index = all.FindIndex(m => m.Id == reported.Id);
        var side = MessagingLimits.ReportContextEachSide;
        var ids = all.Skip(Math.Max(0, index - side)).Take(Math.Min(index, side) + 1 + side).Select(m => m.Id).ToList();
        var messages = (await db.Set<Message>().Where(m => ids.Contains(m.Id)).ToListAsync(ct)).OrderBy(m => m.SentAtUtc).ThenBy(m => m.Id).ToList();
        var senders = messages.Select(m => m.SenderUserId).Distinct().ToList();
        var users = await db.Users.Where(u => senders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var attachments = (await db.Set<MessageAttachment>().Where(a => a.MessageId != null && ids.Contains(a.MessageId.Value)).ToListAsync(ct))
            .GroupBy(a => a.MessageId!.Value).ToDictionary(g => g.Key, g => g.Select(a => a.FileName).ToList());
        return messages.Select(m =>
        {
            users.TryGetValue(m.SenderUserId, out var user);
            return new ReportContextItem(
                m.Id, user?.DisplayNameAr ?? string.Empty, user?.DisplayNameEn ?? string.Empty, m.State == Message.NormalState ? m.Body : null,
                attachments.GetValueOrDefault(m.Id) ?? [], m.SentAtUtc, m.Id == reported.Id);
        }).ToList();
    }

    /// <summary>The units at which the report is visible (L16): never an empty list.</summary>
    private async Task<List<OrgUnit>> UnitsOfAsync(Guid organizationId, Guid senderUserId, Conversation conversation, CancellationToken ct)
    {
        if (conversation.ClassCourseId is { } classCourseId)
        {
            // ISOLATION-BYPASS: the unit of the class course a group belongs to can lie outside the reporter scope; client matched, live rows, one class course by id; only its unit is used.
            var unitId = await db.Set<ClassCourse>().IgnoreQueryFilters()
                .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && c.Id == classCourseId).Select(c => (Guid?)c.OrgUnitId).FirstOrDefaultAsync(ct);
            if (unitId is { } id && await UnitAsync(organizationId, id, ct) is { } context)
            {
                return [context];
            }
        }

        var today = DateOnly.FromDateTime(clock.UtcNow);
        // ISOLATION-BYPASS: every active unit of the sender (L16) lies outside the reporter scope; client matched, live rows, one user, valid today; only the unit ids are used to place the report.
        var unitIds = await db.Set<RoleAssignment>().IgnoreQueryFilters()
            .Where(a => !a.IsDeleted && a.OrganizationId == organizationId && a.UserId == senderUserId && (a.ValidFrom == null || a.ValidFrom <= today) && (a.ValidTo == null || a.ValidTo >= today))
            .Select(a => a.OrgUnitId).Distinct().ToListAsync(ct);
        var units = new List<OrgUnit>();
        foreach (var id in unitIds)
        {
            if (await UnitAsync(organizationId, id, ct) is { } unit)
            {
                units.Add(unit);
            }
        }

        return units.Count > 0 ? units : [await orgTree.GetRootAsync(organizationId, ct)]; // the root fallback: a report is never invisible
    }

    private async Task<OrgUnit?> UnitAsync(Guid organizationId, Guid unitId, CancellationToken ct) =>
        // ISOLATION-BYPASS: reads the unit a report is placed at (its id and path); client matched, live rows, one unit by id.
        await db.OrgUnits.IgnoreQueryFilters().FirstOrDefaultAsync(u => !u.IsDeleted && u.OrganizationId == organizationId && u.Id == unitId, ct);

    private async Task NotifyModeratorsAsync(MessageReport report, IReadOnlyList<OrgUnit> units, CancellationToken ct)
    {
        var safety = report.Reason == ReportReasons.SafetyConcern;
        var key = safety ? NotificationEvents.ModerationSafetyReportReceived : NotificationEvents.ModerationReportReceived;
        foreach (var unit in units)
        {
            try
            {
                await publisher.PublishAsync(new NotificationRequest(
                    key, report.OrganizationId, [], new Dictionary<string, string> { ["Reason"] = report.Reason }, DeepLink: "/moderation", OrgUnitPath: unit.Path), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Notification {Event} for a report failed", key); // a notice failure never undoes the report
            }
        }
    }
}
