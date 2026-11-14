using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Attendance;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Attendance;

/// <summary>
/// Attendance.Absent (FR-EXC-01, L11 item 10, L26): when a session is Taken (or a live proposal is confirmed), the linked guardians and the student are told that
/// the student was marked Absent or Late, once per student record. The text carries the student, course, date and status only: never the note. A failure never
/// undoes the attendance.
/// </summary>
public sealed class AbsenceNotifier(LmsDbContext db, INotificationPublisher publisher, ILogger<AbsenceNotifier> logger)
{
    public async Task NotifyAsync(AttendanceSession session, IReadOnlyCollection<Guid> studentIds, CancellationToken ct)
    {
        if (studentIds.Count == 0)
        {
            return;
        }

        try
        {
            var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == session.ClassCourseId, ct);
            // ISOLATION-BYPASS: the course of a class course is owned by an ancestor unit above the caller's scope; only its names are read. Client matched, deleted rows excluded.
            var course = await db.Set<Course>().IgnoreQueryFilters()
                .Where(c => c.Id == classCourse.CourseId && c.OrganizationId == session.OrganizationId && !c.IsDeleted).FirstOrDefaultAsync(ct);
            var statuses = await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == session.Id && studentIds.Contains(r.StudentProfileId))
                .ToDictionaryAsync(r => r.StudentProfileId, r => r.Status, ct);
            var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
            foreach (var student in students)
            {
                var guardianUsers = await (
                    from link in db.Set<StudentGuardian>()
                    where link.StudentId == student.Id
                    join g in db.Set<GuardianProfile>() on link.GuardianId equals g.Id
                    where g.UserId != null
                    select g.UserId!.Value).Distinct().ToListAsync(ct);
                var recipients = student.UserId is { } own ? guardianUsers.Append(own).Distinct().ToList() : guardianUsers;
                if (recipients.Count == 0 || statuses.GetValueOrDefault(student.Id) is not { } status)
                {
                    continue;
                }

                await publisher.PublishAsync(new NotificationRequest(
                    NotificationEvents.AttendanceAbsent, session.OrganizationId, recipients,
                    new Dictionary<string, string>
                    {
                        ["StudentNameAr"] = student.NameAr, ["StudentNameEn"] = student.NameEn,
                        ["CourseNameAr"] = course?.NameAr ?? string.Empty, ["CourseNameEn"] = course?.NameEn ?? string.Empty,
                        ["Date"] = session.Date.ToString("yyyy-MM-dd"), ["Status"] = status,
                    },
                    OrgUnitPath: session.OrgUnitPath, DeepLink: $"/me/children/{student.Id}/attendance"), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification {Event} could not be published", NotificationEvents.AttendanceAbsent);
        }
    }

    /// <summary>The students whose new status is a reason to tell the family: Absent or Late.</summary>
    public static bool IsNoticeable(string? status) => status is AttendanceStatuses.Absent or AttendanceStatuses.Late;
}
