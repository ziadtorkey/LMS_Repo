using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
using Lms.Domain.Messaging;
using Lms.Domain.Moderation;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Moderation;

public enum ResolveOutcome
{
    Done,
    NotFound,
    NotPending,
    Invalid,
    Conflict,
}

/// <summary>
/// What a moderator can do with a report (M6 Task 15, FR-MSG-04, D66, L16): Dismiss ("Keep"), Remove (the message becomes a tombstone for the participants, the original stays for moderators
/// until retention purges it), Warn, Restrict (1, 7 or 30 days), RemoveAndRestrict and LockConversation. Moderators act on messages and on a person's messaging right, <b>never on a block</b>:
/// nothing here reads, writes or removes a <see cref="UserBlock"/>. The decision and its effects are one save (the audit rows with it); the notices follow it and a failure of one never
/// undoes the decision. The reporter learns "Reviewed" only; the sender learns of a removal, a warning or a restriction and never who reported.
/// </summary>
public sealed class ModerationActionsService(
    LmsDbContext db, ModerationQueue queue, ICurrentContext context, IClock clock, INotificationPublisher publisher, ILogger<ModerationActionsService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResolveOutcome> ResolveAsync(Guid reportId, string? action, int? durationDays, CancellationToken ct)
    {
        if (context.UserId is not { } me)
        {
            return ResolveOutcome.NotFound;
        }

        if (action is null || !ModerationActions.All.Contains(action) || ModerationActions.Restricting.Contains(action) != durationDays.HasValue
            || durationDays is { } days && !RestrictionDurations.IsValid(days))
        {
            return ResolveOutcome.Invalid;
        }

        var report = await queue.Visible().FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null)
        {
            return ResolveOutcome.NotFound;
        }

        if (!report.IsPending)
        {
            return ResolveOutcome.NotPending;
        }

        var now = clock.UtcNow;
        var effects = await ApplyAsync(report, action, durationDays, me, now, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ResolveOutcome.Conflict; // another moderator changed the same rows at once
        }

        await NotifyAsync(report, effects, ct);
        return ResolveOutcome.Done;
    }

    /// <summary>Dismisses the pending reports in scope (others are skipped, never an error). Returns how many were dismissed.</summary>
    public async Task<int> BulkDismissAsync(IReadOnlyCollection<Guid> reportIds, CancellationToken ct)
    {
        if (context.UserId is not { } me)
        {
            return 0;
        }

        var ids = reportIds.Distinct().Take(ModerationLimits.BulkDismissMax).ToList();
        var reports = await queue.Visible().Where(r => ids.Contains(r.Id) && (r.Status == MessageReport.OpenStatus || r.Status == MessageReport.InReviewStatus)).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var report in reports)
        {
            await ApplyAsync(report, ModerationActions.Dismiss, null, me, now, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0;
        }

        foreach (var report in reports)
        {
            await NotifyAsync(report, new Effects(null, null, false), ct);
        }

        return reports.Count;
    }

    /// <summary>Lifts the restriction of a user who is in scope (a user of one of the visible reports); false when there is nothing to lift in the caller's scope.</summary>
    public async Task<bool> LiftRestrictionAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (!await queue.Visible().AnyAsync(r => r.ReportedUserId == userId, ct))
        {
            return false;
        }

        var restriction = await db.Set<MessagingRestriction>().FirstOrDefaultAsync(r => r.UserId == userId && (r.UntilUtc == null || r.UntilUtc > now), ct);
        if (restriction is null)
        {
            return false;
        }

        restriction.Lift(now);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private sealed record Effects(Guid? WarnedUserId, DateTime? RestrictedUntilUtc, bool Warned);

    private async Task<Effects> ApplyAsync(MessageReport report, string action, int? durationDays, Guid moderatorUserId, DateTime now, CancellationToken ct)
    {
        Guid? warned = null;
        DateTime? until = null;
        var tell = false;
        if (action == ModerationActions.Dismiss)
        {
            report.Dismiss(moderatorUserId, now);
        }
        else
        {
            report.TakeAction(action, moderatorUserId, now);
            if (action is ModerationActions.Remove or ModerationActions.RemoveAndRestrict)
            {
                var message = await db.Set<Message>().FirstOrDefaultAsync(m => m.Id == report.MessageId, ct);
                if (message is { State: Message.NormalState })
                {
                    message.RemoveByModerator();
                }

                warned = report.ReportedUserId;
                tell = true;
            }

            if (action == ModerationActions.Warn)
            {
                warned = report.ReportedUserId;
                tell = true;
            }

            if (action is ModerationActions.Restrict or ModerationActions.RemoveAndRestrict)
            {
                until = now.AddDays(durationDays!.Value);
                var existing = await db.Set<MessagingRestriction>().FirstOrDefaultAsync(r => r.UserId == report.ReportedUserId && (r.UntilUtc == null || r.UntilUtc > now), ct);
                if (existing is null)
                {
                    db.Add(new MessagingRestriction(report.OrganizationId, report.ReportedUserId, until));
                }
                else
                {
                    existing.RestrictUntil(until);
                }
            }

            if (action == ModerationActions.LockConversation)
            {
                (await db.Set<Conversation>().FirstOrDefaultAsync(c => c.Id == report.ConversationId, ct))?.Lock();
            }
        }

        // The decision is audited without any text: the report, the action and the moderator (the report entity itself keeps its text out of the audit log).
        db.Add(new AuditEntry(
            report.OrganizationId, moderatorUserId, context.AssignmentId, AuditAction.Updated, "MessageReport", report.Id, null,
            JsonSerializer.Serialize(new { action, durationDays, status = report.Status }, Json), now, null, context.ScopePath));
        return new Effects(warned, until, tell);
    }

    private async Task NotifyAsync(MessageReport report, Effects effects, CancellationToken ct)
    {
        await TrySendAsync(new NotificationRequest(
            NotificationEvents.MessagingReportResolved, report.OrganizationId, [report.ReporterUserId],
            new Dictionary<string, string> { ["Outcome"] = ReportOutcomes.Reviewed }), ct); // the reporter learns "Reviewed" only: no action, no note
        if (effects.Warned && effects.WarnedUserId is { } warned)
        {
            await TrySendAsync(new NotificationRequest(NotificationEvents.MessagingWarning, report.OrganizationId, [warned], new Dictionary<string, string>()), ct);
        }

        if (effects.RestrictedUntilUtc is { } until)
        {
            await TrySendAsync(new NotificationRequest(
                NotificationEvents.MessagingRestricted, report.OrganizationId, [report.ReportedUserId],
                new Dictionary<string, string> { ["UntilUtc"] = until.ToString("yyyy-MM-dd") }), ct);
        }
    }

    private async Task TrySendAsync(NotificationRequest request, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} for a moderation decision failed", request.EventKey);
        }
    }
}
