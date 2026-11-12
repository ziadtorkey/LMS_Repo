using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Forums;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Forums;

/// <summary>Forums.Announcement to the students actively enrolled in the ClassCourse (FR-NOT-01). A failure never undoes the announcement.</summary>
internal sealed class AnnouncementNotifier(LmsDbContext db, INotificationPublisher publisher, ILogger<AnnouncementNotifier> logger)
{
    public async Task NotifyAsync(CourseAnnouncement announcement, ClassCourse classCourse, CancellationToken ct)
    {
        try
        {
            var studentUsers = await (
                from e in db.Set<CourseEnrollment>()
                where e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus
                join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                where s.UserId != null
                select s.UserId!.Value).Distinct().ToListAsync(ct);
            if (studentUsers.Count == 0)
            {
                return;
            }

            // ISOLATION-BYPASS: the course of a class course is owned by an ancestor unit that may lie above the caller's scope;
            // only its names are read. Client matched, deleted courses excluded by hand.
            var course = await db.Set<Course>().IgnoreQueryFilters()
                .Where(c => c.Id == classCourse.CourseId && c.OrganizationId == classCourse.OrganizationId && !c.IsDeleted)
                .FirstOrDefaultAsync(ct);
            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.ForumsAnnouncement, announcement.OrganizationId, studentUsers,
                new Dictionary<string, string>
                {
                    ["Title"] = announcement.Title, ["CourseNameAr"] = course?.NameAr ?? string.Empty, ["CourseNameEn"] = course?.NameEn ?? string.Empty,
                },
                OrgUnitPath: announcement.OrgUnitPath), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} could not be published", NotificationEvents.ForumsAnnouncement);
        }
    }
}
