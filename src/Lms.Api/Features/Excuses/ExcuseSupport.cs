using Lms.Domain.Attendance;
using Lms.Domain.Excuses;
using Lms.Infrastructure.Excuses;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Excuses;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Excuses;

/// <summary>Maps excuses to their DTOs: the student and the people involved are named here, nothing else leaves (no attachment file id, no unit path).</summary>
internal static class ExcuseMapper
{
    public static async Task<IReadOnlyList<ExcuseDto>> ToDtosAsync(
        LmsDbContext db, IReadOnlyList<AbsenceExcuse> excuses, Guid callerUserId, DateTime nowUtc, CancellationToken ct)
    {
        if (excuses.Count == 0)
        {
            return [];
        }

        var studentIds = excuses.Select(e => e.StudentProfileId).Distinct().ToList();
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var userIds = excuses.SelectMany(e => new[] { e.SubmittedByUserId, e.ReviewerUserId, e.EscalatedToUserId, e.EscalatedByUserId })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToDictionaryAsync(u => u.Id, ct);
        var fileNames = await AttachmentNamesAsync(db, excuses, ct);
        var sessions = await SessionsAsync(db, excuses, ct);

        (string Ar, string En) Name(Guid? id) =>
            id is { } v && users.TryGetValue(v, out var u) ? (u.DisplayNameAr, u.DisplayNameEn) : (string.Empty, string.Empty);

        return excuses.Select(e =>
        {
            students.TryGetValue(e.StudentProfileId, out var student);
            var submitter = Name(e.SubmittedByUserId);
            var reviewer = Name(e.ReviewerUserId);
            var target = Name(e.EscalatedToUserId);
            var history = new List<ExcuseHistoryDto> { new(ExcuseStatuses.Submitted, e.SubmittedByUserId, submitter.Ar, submitter.En, e.ReasonText, e.SubmittedAtUtc) };
            if (e.EscalatedAtUtc is { } escalatedAt && e.EscalatedByUserId is { } by)
            {
                var actor = Name(by);
                history.Add(new ExcuseHistoryDto(ExcuseStatuses.Escalated, by, actor.Ar, actor.En, e.EscalationComment, escalatedAt));
            }

            if (e.DecidedAtUtc is { } decidedAt)
            {
                var actorId = e.Status == ExcuseStatuses.Withdrawn ? e.SubmittedByUserId : e.ReviewerUserId ?? e.SubmittedByUserId;
                var actor = Name(actorId);
                history.Add(new ExcuseHistoryDto(e.Status, actorId, actor.Ar, actor.En, e.DecisionComment, decidedAt));
            }

            return new ExcuseDto(
                e.Id, e.StudentProfileId, student?.NameAr ?? string.Empty, student?.NameEn ?? string.Empty, student?.StudentCode ?? string.Empty,
                e.SubmittedByUserId, submitter.Ar, submitter.En, e.Reason, e.ReasonText, e.DateFrom, e.DateTo, e.Status,
                sessions.GetValueOrDefault(e.Id) ?? [],
                e.AttachmentFileId is not null, e.AttachmentFileId is { } f ? fileNames.GetValueOrDefault(f) : null,
                e.DecisionComment, e.ReviewerUserId, reviewer.Ar, reviewer.En, e.EscalatedToUserId, target.Ar, target.En,
                e.SubmittedAtUtc, e.DecidedAtUtc, e.Status == ExcuseStatuses.Submitted && e.SubmittedByUserId == callerUserId, history, nowUtc);
        }).ToList();
    }

    /// <summary>The sessions each excuse covers with the record's current status (it becomes Excused on approval); only names and the status leave.</summary>
    private static async Task<Dictionary<Guid, List<ExcuseSessionDto>>> SessionsAsync(LmsDbContext db, IReadOnlyList<AbsenceExcuse> excuses, CancellationToken ct)
    {
        var excuseIds = excuses.Select(e => e.Id).ToList();
        var rows = await (from link in db.Set<AbsenceExcuseSession>()
                          join s in db.Set<AttendanceSession>() on link.AttendanceSessionId equals s.Id
                          where excuseIds.Contains(link.AbsenceExcuseId)
                          select new { link.AbsenceExcuseId, SessionId = s.Id, s.Date, s.ClassCourseId }).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        var studentOf = excuses.ToDictionary(e => e.Id, e => e.StudentProfileId);
        var studentIds = studentOf.Values.Distinct().ToList();
        var sessionIds = rows.Select(r => r.SessionId).Distinct().ToList();
        var statuses = (await db.Set<AttendanceRecord>().Where(r => sessionIds.Contains(r.AttendanceSessionId) && studentIds.Contains(r.StudentProfileId))
                .Select(r => new { r.AttendanceSessionId, r.StudentProfileId, r.Status }).ToListAsync(ct))
            .ToDictionary(r => (r.AttendanceSessionId, r.StudentProfileId), r => r.Status);
        var names = await ExcuseSubmissionService.CourseNamesAsync(db, excuses[0].OrganizationId, rows.Select(r => r.ClassCourseId).Distinct().ToList(), ct);
        return rows.GroupBy(r => r.AbsenceExcuseId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Date).ThenBy(r => r.SessionId).Select(r =>
        {
            var name = names.GetValueOrDefault(r.ClassCourseId);
            return new ExcuseSessionDto(r.SessionId, r.Date, name.Ar ?? string.Empty, name.En ?? string.Empty, statuses.GetValueOrDefault((r.SessionId, studentOf[g.Key])) ?? string.Empty);
        }).ToList());
    }

    private static async Task<Dictionary<Guid, string>> AttachmentNamesAsync(LmsDbContext db, IReadOnlyList<AbsenceExcuse> excuses, CancellationToken ct)
    {
        var ids = excuses.Where(e => e.AttachmentFileId is not null).Select(e => e.AttachmentFileId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Set<Lms.Domain.Files.StoredFile>().Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.FileName, ct);
    }
}

/// <summary>Excuses.Decided to the submitter and Excuses.Escalated to the new reviewer (FR-EXC-03). A notification failure never undoes the change.</summary>
internal sealed class ExcuseNotifier(INotificationPublisher publisher, ILogger<ExcuseNotifier> logger)
{
    public Task DecidedAsync(AbsenceExcuse excuse, StudentProfile student, CancellationToken ct) =>
        SendAsync(new NotificationRequest(
            NotificationEvents.ExcusesDecided, excuse.OrganizationId, [excuse.SubmittedByUserId],
            new Dictionary<string, string>(Names(student)) { ["Decision"] = excuse.Status }), ct);

    public Task EscalatedAsync(AbsenceExcuse excuse, StudentProfile student, Guid targetUserId, CancellationToken ct) =>
        SendAsync(new NotificationRequest(NotificationEvents.ExcusesEscalated, excuse.OrganizationId, [targetUserId], Names(student)), ct);

    private static Dictionary<string, string> Names(StudentProfile s) => new() { ["StudentNameAr"] = s.NameAr, ["StudentNameEn"] = s.NameEn };

    private async Task SendAsync(NotificationRequest request, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {EventKey} for an excuse failed", request.EventKey);
        }
    }
}
