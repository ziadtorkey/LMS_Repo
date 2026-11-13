using Lms.Domain.Abstractions;
using Lms.Domain.Assignments;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>Assignments.Published (FR-NOT-01) after the commit. A notification failure never undoes the change.</summary>
public sealed class CourseAssignmentNotifier(
    LmsDbContext db, INotificationPublisher publisher, IClock clock, ILogger<CourseAssignmentNotifier> logger)
{
    /// <summary>To the students of the class course (or of the target group) who can see the assignment now; none yet when it is scheduled.</summary>
    public async Task PublishedAsync(Assignment assignment, LessonModule module, ClassCourse classCourse, CancellationToken ct)
    {
        if (!module.IsVisibleNow(clock.UtcNow))
        {
            return;
        }

        try
        {
            var enrolled = db.Set<CourseEnrollment>()
                .Where(e => e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus);
            if (module.GroupId is { } groupId)
            {
                var members = db.Set<GroupMember>().Where(m => m.GroupId == groupId).Select(m => m.StudentId);
                enrolled = enrolled.Where(e => members.Contains(e.StudentId));
            }

            var users = await (from e in enrolled
                               join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                               where s.UserId != null
                               select s.UserId!.Value).Distinct().ToListAsync(ct);
            if (users.Count == 0)
            {
                return;
            }

            var course = await db.Set<Course>().FirstAsync(c => c.Id == classCourse.CourseId, ct);
            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.AssignmentsPublished, assignment.OrganizationId, users,
                new Dictionary<string, string>
                {
                    ["TitleAr"] = module.TitleAr, ["TitleEn"] = module.TitleEn, ["CourseNameAr"] = course.NameAr, ["CourseNameEn"] = course.NameEn,
                },
                DeepLink: $"/class-courses/{classCourse.Id}/assignments/{assignment.Id}", OrgUnitPath: assignment.OrgUnitPath), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} could not be published", NotificationEvents.AssignmentsPublished);
        }
    }
}
