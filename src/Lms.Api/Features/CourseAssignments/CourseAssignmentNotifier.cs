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

            var course = await CourseAsync(classCourse, ct);
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

    /// <summary>Assignments.Graded to the student whose work was returned (after the commit; a failure never undoes the return).</summary>
    public async Task GradedAsync(Assignment assignment, LessonModule module, ClassCourse classCourse, Guid studentId, CancellationToken ct)
    {
        try
        {
            var userId = await db.Set<StudentProfile>().Where(s => s.Id == studentId).Select(s => s.UserId).FirstOrDefaultAsync(ct);
            if (userId is not { } user)
            {
                return;
            }

            var course = await CourseAsync(classCourse, ct);
            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.AssignmentsGraded, assignment.OrganizationId, [user],
                new Dictionary<string, string>
                {
                    ["TitleAr"] = module.TitleAr, ["TitleEn"] = module.TitleEn, ["CourseNameAr"] = course.NameAr, ["CourseNameEn"] = course.NameEn,
                },
                DeepLink: $"/class-courses/{classCourse.Id}/assignments/{assignment.Id}", OrgUnitPath: assignment.OrgUnitPath), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} could not be published", NotificationEvents.AssignmentsGraded);
        }
    }

    /// <summary>The course of a class course: owned by a unit that can sit above the caller's scope, so the normal filter would hide it.</summary>
    private Task<Course> CourseAsync(ClassCourse classCourse, CancellationToken ct) =>
        // ISOLATION-BYPASS: the course of a class course is owned by an ancestor unit above the caller's scope; only its names go
        // into the notice, for a class course the caller already reached. Client matched, deleted courses excluded by hand.
        db.Set<Course>().IgnoreQueryFilters()
            .FirstAsync(c => c.Id == classCourse.CourseId && c.OrganizationId == classCourse.OrganizationId && !c.IsDeleted, ct);
}
