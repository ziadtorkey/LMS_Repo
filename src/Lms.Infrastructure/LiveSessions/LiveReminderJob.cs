using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.LiveSessions;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.LiveSessions;

/// <summary>
/// The <c>Live.Reminder</c> job (M4b Task 6, FR-NOT-01): scheduled for <c>Live.ReminderMinutes</c> before the start, it tells the
/// targeted students who can see the session module, once. It runs with the system context, so it names the client in its payload
/// and does nothing when the session belongs to another one (a job argument is not trusted to pick the tenant). The reminder is
/// claimed with a conditional update, so a retry or a re-scheduled duplicate sends nothing twice.
/// </summary>
public sealed class LiveReminderJob(
    LmsDbContext db, INotificationPublisher publisher, IClock clock, ILogger<LiveReminderJob> logger)
{
    public async Task RunAsync(Guid sessionId, Guid organizationId)
    {
        var now = clock.UtcNow;
        var session = await db.Set<LiveSession>().FirstOrDefaultAsync(s => s.Id == sessionId);
        if (session is null || session.OrganizationId != organizationId || session.Status != LiveSessionRules.Scheduled
            || session.StartsAtUtc <= now || session.ReminderSentAtUtc is not null)
        {
            return;
        }

        var module = await db.Set<LessonModule>().FirstOrDefaultAsync(m => m.Kind == ModuleKinds.LiveSession && m.RefId == session.Id);
        if (module is null || !module.IsVisibleNow(now))
        {
            return;
        }

        var claimed = await db.Set<LiveSession>().Where(s => s.Id == sessionId && s.ReminderSentAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.ReminderSentAtUtc, now));
        if (claimed == 0)
        {
            return;
        }

        try
        {
            var enrolled = db.Set<CourseEnrollment>().Where(e => e.ClassCourseId == session.ClassCourseId && e.Status == CourseEnrollment.ActiveStatus);
            if (module.GroupId is { } groupId)
            {
                var members = db.Set<GroupMember>().Where(m => m.GroupId == groupId).Select(m => m.StudentId);
                enrolled = enrolled.Where(e => members.Contains(e.StudentId));
            }

            var users = await (from e in enrolled
                               join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                               where s.UserId != null
                               select s.UserId!.Value).Distinct().ToListAsync();
            if (users.Count == 0)
            {
                return;
            }

            var course = await (from cc in db.Set<ClassCourse>()
                                join c in db.Set<Course>() on cc.CourseId equals c.Id
                                where cc.Id == session.ClassCourseId
                                select c).FirstAsync();
            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.LiveReminder, session.OrganizationId, users,
                new Dictionary<string, string>
                {
                    ["TitleAr"] = module.TitleAr, ["TitleEn"] = module.TitleEn, ["CourseNameAr"] = course.NameAr, ["CourseNameEn"] = course.NameEn,
                    ["StartsAtUtc"] = DateTime.SpecifyKind(session.StartsAtUtc, DateTimeKind.Utc).ToString("O"),
                },
                OrgUnitPath: session.OrgUnitPath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} for live session {SessionId} could not be published", NotificationEvents.LiveReminder, sessionId);
        }
    }
}
