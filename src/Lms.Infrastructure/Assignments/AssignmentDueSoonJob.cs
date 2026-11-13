using System.Globalization;
using Lms.Domain.Abstractions;
using Lms.Domain.Assignments;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Assignments;

/// <summary>
/// Hourly "due soon" reminder (M4a Task 9, FR-ASG-02), run as a system job (platform context) per client: an assignment whose
/// due time is within the next 24 hours notifies the targeted, enrolled students who have not handed in yet, once. The assignment is
/// marked first, so a failed notice is skipped rather than repeated every hour. Only assignments whose module is visible now are
/// reminded; a later change of the due date earns a new reminder.
/// </summary>
public sealed class AssignmentDueSoonJob(
    LmsDbContext db, INotificationPublisher publisher, IClock clock, ILogger<AssignmentDueSoonJob> logger)
{
    public const string RecurringJobId = "assignment-due-soon";

    /// <summary>Every hour at minute 10 (the cron is read in <c>Jobs:TimeZone</c>).</summary>
    public const string Cron = "10 * * * *";

    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <param name="asOf">The instant to evaluate; default now. The dev trigger passes a day for E2E (start of that day, UTC).</param>
    public Task RunAsync(DateOnly? asOf, CancellationToken cancellationToken) =>
        RunAtAsync(asOf is { } day ? day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) : clock.UtcNow, cancellationToken);

    /// <summary>Runs the reminders as of the given instant (UTC).</summary>
    public async Task RunAtAsync(DateTime now, CancellationToken cancellationToken)
    {
        var organizationIds = await db.Set<Organization>().Select(o => o.Id).ToListAsync(cancellationToken);
        foreach (var organizationId in organizationIds)
        {
            try
            {
                await ProcessOrganizationAsync(organizationId, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One client's failure must not stop the others; the next run retries it.
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Assignment due-soon reminders failed for client {OrganizationId}", organizationId);
            }
        }
    }

    private async Task ProcessOrganizationAsync(Guid organizationId, DateTime now, CancellationToken ct)
    {
        var until = now.Add(Window);
        var due = await db.Set<Assignment>()
            .Where(a => a.OrganizationId == organizationId && a.DueAtUtc != null && a.DueAtUtc > now && a.DueAtUtc <= until
                        && a.DueSoonNotifiedAtUtc == null)
            .ToListAsync(ct);
        foreach (var assignment in due)
        {
            var module = await db.Set<LessonModule>()
                .FirstOrDefaultAsync(m => m.Kind == ModuleKinds.Assignment && m.RefId == assignment.Id, ct);
            if (module is null || !module.IsVisibleNow(now))
            {
                continue; // hidden or not yet visible: nobody was told about it, so nobody is reminded; checked again next run
            }

            assignment.MarkDueSoonNotified(now);
            await db.SaveChangesAsync(ct);
            await NotifyAsync(assignment, module, ct);
        }
    }

    private async Task NotifyAsync(Assignment assignment, LessonModule module, CancellationToken ct)
    {
        var enrolled = db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == assignment.ClassCourseId && e.Status == CourseEnrollment.ActiveStatus);
        if (module.GroupId is { } groupId)
        {
            var members = db.Set<GroupMember>().Where(m => m.GroupId == groupId).Select(m => m.StudentId);
            enrolled = enrolled.Where(e => members.Contains(e.StudentId));
        }

        var handedIn = db.Set<Submission>()
            .Where(s => s.AssignmentId == assignment.Id && s.Number > 0)
            .Select(s => s.StudentId);
        var users = await (from e in enrolled
                           join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                           where s.UserId != null && !handedIn.Contains(s.Id)
                           select s.UserId!.Value).Distinct().ToListAsync(ct);
        if (users.Count == 0)
        {
            return;
        }

        var classCourse = await db.Set<ClassCourse>().FirstAsync(c => c.Id == assignment.ClassCourseId, ct);
        var course = await db.Set<Course>().FirstAsync(c => c.Id == classCourse.CourseId, ct);
        await publisher.PublishAsync(new NotificationRequest(
            NotificationEvents.AssignmentsDueSoon, assignment.OrganizationId, users,
            new Dictionary<string, string>
            {
                ["TitleAr"] = module.TitleAr, ["TitleEn"] = module.TitleEn, ["CourseNameAr"] = course.NameAr, ["CourseNameEn"] = course.NameEn,
                ["DueAt"] = assignment.DueAtUtc!.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            },
            DeepLink: $"/class-courses/{classCourse.Id}/assignments/{assignment.Id}", OrgUnitPath: assignment.OrgUnitPath), ct);
    }
}
