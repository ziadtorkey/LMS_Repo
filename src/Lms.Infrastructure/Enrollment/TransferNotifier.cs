using System.Globalization;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Enrollment;

/// <summary>
/// Tells the guardians (with portal accounts) and the current teachers of both classes that a student moved
/// (<c>Enrollment.StudentTransferred</c>). Called after the transfer committed; a failure is logged and never undoes the transfer.
/// </summary>
public sealed class TransferNotifier(
    LmsDbContext db, INotificationPublisher publisher, IClock clock, ILogger<TransferNotifier> logger)
{
    public async Task NotifyAsync(StudentProfile student, Class from, Class to, DateOnly effectiveOn, CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(clock.UtcNow);
            var guardianUsers = await (
                    from link in db.Set<StudentGuardian>()
                    join guardian in db.Set<GuardianProfile>() on link.GuardianId equals guardian.Id
                    where link.StudentId == student.Id && guardian.UserId != null
                    select guardian.UserId!.Value)
                .ToListAsync(ct);
            var classIds = new[] { from.Id, to.Id };
            var teachers = await (
                    from teacher in db.Set<ClassCourseTeacher>()
                    join classCourse in db.Set<ClassCourse>() on teacher.ClassCourseId equals classCourse.Id
                    where classIds.Contains(classCourse.ClassId) && teacher.From <= today && (teacher.To == null || teacher.To >= today)
                    select teacher.UserId)
                .ToListAsync(ct);

            var recipients = guardianUsers.Concat(teachers).Distinct().ToList();
            if (recipients.Count == 0)
            {
                return;
            }

            await publisher.PublishAsync(
                new NotificationRequest(
                    NotificationEvents.EnrollmentStudentTransferred, student.OrganizationId, recipients,
                    new Dictionary<string, string>
                    {
                        ["StudentNameAr"] = student.NameAr,
                        ["StudentNameEn"] = student.NameEn,
                        ["FromClassAr"] = from.NameAr,
                        ["FromClassEn"] = from.NameEn,
                        ["ToClassAr"] = to.NameAr,
                        ["ToClassEn"] = to.NameEn,
                        ["EffectiveOn"] = effectiveOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    },
                    OrgUnitPath: to.OrgUnitPath),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The transfer notification for student {StudentId} could not be published", student.Id);
        }
    }
}
