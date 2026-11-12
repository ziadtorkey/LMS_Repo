using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// Tells the targeted students that a quiz is open (event Quizzes.Opened): once per quiz, when it is published already open, or when
/// the open time or the module schedule arrives. A quiz is due when its module is visible now, it has opened and not closed, and it has
/// a question. The notified mark is set by one conditional update, so concurrent callers (the builder, the sweep) publish once.
/// </summary>
public sealed class QuizOpenedNotifier(LmsDbContext db, INotificationPublisher publisher, IClock clock, ILogger<QuizOpenedNotifier> logger)
{
    private const int Batch = 500;

    /// <summary>Notifies for the due quizzes (one quiz when a quiz id is given); returns how many were sent.</summary>
    public async Task<int> NotifyDueAsync(Guid? quizId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var candidates = await (from q in db.Set<Quiz>()
                                join m in db.Set<LessonModule>() on q.ModuleId equals m.Id
                                where q.OpenedNotifiedAtUtc == null && (quizId == null || q.Id == quizId)
                                      && (q.OpensAtUtc == null || q.OpensAtUtc <= now) && (q.ClosesAtUtc == null || q.ClosesAtUtc > now)
                                orderby q.Id
                                select new { Quiz = q, Module = m }).Take(Batch).ToListAsync(ct);
        var sent = 0;
        foreach (var candidate in candidates.Where(c => c.Module.IsVisibleNow(now)))
        {
            if (!await db.Set<QuizQuestion>().AnyAsync(x => x.QuizId == candidate.Quiz.Id, ct))
            {
                continue; // nothing to take yet: the first question will make it due
            }

            var stamp = QuizAttempt.Micros(now);
            var won = await db.Set<Quiz>().Where(q => q.Id == candidate.Quiz.Id && q.OpenedNotifiedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.OpenedNotifiedAtUtc, stamp), ct);
            if (won == 1)
            {
                await PublishAsync(candidate.Quiz, candidate.Module, ct);
                sent++;
            }
        }

        return sent;
    }

    private async Task PublishAsync(Quiz quiz, LessonModule module, CancellationToken ct)
    {
        try
        {
            var users = await RecipientsAsync(quiz, module, ct);
            if (users.Count == 0)
            {
                return;
            }

            var courseId = await db.Set<ClassCourse>().Where(c => c.Id == quiz.ClassCourseId).Select(c => c.CourseId).FirstAsync(ct);
            // ISOLATION-BYPASS: the course is usually owned by a unit above the class (and above the author's scope); limited to this
            // client, the one live course of the class course, and only its two names leave.
            var course = await db.Set<Course>().IgnoreQueryFilters()
                .Where(co => co.Id == courseId && co.OrganizationId == quiz.OrganizationId && !co.IsDeleted)
                .Select(co => new { co.NameAr, co.NameEn }).FirstAsync(ct);
            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.QuizzesOpened, quiz.OrganizationId, users,
                new Dictionary<string, string>
                {
                    ["TitleAr"] = module.TitleAr,
                    ["TitleEn"] = module.TitleEn,
                    ["CourseNameAr"] = course.NameAr,
                    ["CourseNameEn"] = course.NameEn,
                },
                DeepLink: $"/class-courses/{quiz.ClassCourseId}/plan", OrgUnitPath: quiz.OrgUnitPath), ct);
        }
        catch (Exception ex)
        {
            // The quiz is open either way; the students simply miss the notification.
            logger.LogWarning(ex, "Notification {Event} for quiz {QuizId} could not be published", NotificationEvents.QuizzesOpened, quiz.Id);
        }
    }

    /// <summary>The students the module targets: the whole class course (active enrollments) or its group.</summary>
    private async Task<List<Guid>> RecipientsAsync(Quiz quiz, LessonModule module, CancellationToken ct)
    {
        var students = module.Target == ModuleTargets.Group && module.GroupId is { } groupId
            ? from m in db.Set<GroupMember>()
              join s in db.Set<StudentProfile>() on m.StudentId equals s.Id
              where m.GroupId == groupId
              select s.UserId
            : from e in db.Set<CourseEnrollment>()
              join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
              where e.ClassCourseId == quiz.ClassCourseId && e.Status == CourseEnrollment.ActiveStatus
              select s.UserId;
        return (await students.ToListAsync(ct)).Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
    }
}
