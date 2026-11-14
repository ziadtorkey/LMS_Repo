using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Publishing;

/// <summary>
/// One <c>Grades.Published</c> notification per student per publish batch (L26): the student and the guardians linked to them are the recipients.
/// The text carries the course name only, never a grade value or an item title, so a hidden item is not revealed by its notice.
/// </summary>
public sealed class GradePublishedNotifier(LmsDbContext db, INotificationPublisher publisher)
{
    /// <returns>The number of students told (a student without an account and without a guardian account is skipped).</returns>
    public async Task<int> NotifyAsync(ClassCourse classCourse, IReadOnlyList<Guid> studentIds, CancellationToken ct)
    {
        if (studentIds.Count == 0)
        {
            return 0;
        }

        var course = await db.Set<Course>().FirstAsync(c => c.Id == classCourse.CourseId, ct);
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).Select(s => new { s.Id, s.UserId }).ToListAsync(ct);
        var guardians = await (from link in db.Set<StudentGuardian>()
                               join g in db.Set<GuardianProfile>() on link.GuardianId equals g.Id
                               where studentIds.Contains(link.StudentId) && g.UserId != null
                               select new { link.StudentId, UserId = g.UserId!.Value }).ToListAsync(ct);
        var byStudent = guardians.ToLookup(g => g.StudentId, g => g.UserId);
        var sent = 0;
        foreach (var student in students)
        {
            var recipients = byStudent[student.Id].ToList();
            if (student.UserId is { } own)
            {
                recipients.Add(own);
            }

            recipients = recipients.Distinct().ToList();
            if (recipients.Count == 0)
            {
                continue;
            }

            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.GradesPublished, classCourse.OrganizationId, recipients,
                new Dictionary<string, string> { ["CourseNameAr"] = course.NameAr, ["CourseNameEn"] = course.NameEn },
                OrgUnitPath: classCourse.OrgUnitPath), ct);
            sent++;
        }

        return sent;
    }
}
