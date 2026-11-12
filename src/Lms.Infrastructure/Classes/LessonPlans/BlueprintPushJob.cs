using System.Globalization;
using Lms.Domain.AcademicYears;
using Lms.Domain.Abstractions;
using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Notifications;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Blueprints;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.Classes.LessonPlans;

/// <summary>
/// Pushes a published blueprint version into the class lesson plans of its course (FR-LP-04). Runs as a system job (platform
/// context) for one client: for every live plan whose class is Active and whose academic year is not Closed, sections and
/// lessons are merged by key (new ones added in the blueprint's order, titles, objectives, offsets and order updated, keys no
/// longer in the blueprint hidden, keys that came back un-hidden), one transaction per plan. A plan already at this version or
/// newer is skipped, so running it twice changes nothing and notifies nobody twice; with <c>NewClassesOnly</c> only plans that
/// have no version yet are filled. The current teachers of each class course are told once (<c>Blueprints.Pushed</c>).
/// </summary>
public sealed class BlueprintPushJob(
    LmsDbContext db, LessonPlanReads reads, IClock clock, INotificationPublisher publisher, ILogger<BlueprintPushJob> logger)
{
    public async Task RunAsync(Guid blueprintId, CancellationToken cancellationToken)
    {
        // A system job (platform context): it sees every class of the client, whatever scope the publisher had.
        var blueprint = await db.Set<Blueprint>().FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);
        if (blueprint is null || blueprint.Status != Blueprint.PublishedStatus)
        {
            return; // deleted, or superseded before the job ran: a newer push is queued by its own publish
        }

        var tree = await reads.TreeAsync(blueprint, cancellationToken);
        var onlyEmpty = blueprint.ApplyMode == BlueprintApplyModes.NewClassesOnly;
        var targets = await (
                from plan in db.Set<ClassLessonPlan>()
                join cc in db.Set<ClassCourse>() on plan.ClassCourseId equals cc.Id
                join cls in db.Set<Class>() on cc.ClassId equals cls.Id
                where plan.OrganizationId == blueprint.OrganizationId && cc.CourseId == blueprint.CourseId
                      && cls.Status == Class.ActiveStatus
                      && (plan.BlueprintVersion == null || (!onlyEmpty && plan.BlueprintVersion < blueprint.Version))
                select new { plan.Id, ClassCourseId = cc.Id, cls.AcademicYearId })
            .ToListAsync(cancellationToken);

        var closedYears = (await db.Set<AcademicYear>()
                .Where(y => y.OrganizationId == blueprint.OrganizationId && y.Status == AcademicYearStatus.Closed)
                .Select(y => y.Id).ToListAsync(cancellationToken)).ToHashSet();

        var pushed = new List<Guid>();
        foreach (var target in targets.Where(t => !closedYears.Contains(t.AcademicYearId)))
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var plan = await db.Set<ClassLessonPlan>().FirstAsync(p => p.Id == target.Id, cancellationToken);
                await MergeAsync(plan, tree, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                pushed.Add(target.ClassCourseId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Pushing blueprint {BlueprintId} into plan {PlanId} failed", blueprintId, target.Id);
            }
        }

        await NotifyTeachersAsync(blueprint, pushed, cancellationToken);
    }

    private async Task MergeAsync(ClassLessonPlan plan, BlueprintTree tree, CancellationToken ct)
    {
        var sections = await db.Set<ClassSection>().Where(s => s.PlanId == plan.Id).ToDictionaryAsync(s => s.Key, ct);
        var lessons = await db.Set<ClassLesson>().Where(l => l.PlanId == plan.Id).ToDictionaryAsync(l => l.Key, ct);
        var sectionKeys = tree.Sections.ToDictionary(s => s.Id, s => s.Key);

        foreach (var section in tree.Sections)
        {
            if (sections.TryGetValue(section.Key, out var existing))
            {
                existing.Update(section.Order, section.TitleAr, section.TitleEn, isHidden: false);
                continue;
            }

            var added = new ClassSection(plan, section.Key, section.Order, section.TitleAr, section.TitleEn);
            sections[section.Key] = added;
            db.Set<ClassSection>().Add(added);
        }

        foreach (var removed in sections.Values.Where(s => tree.Sections.All(b => b.Key != s.Key) && !s.IsHidden))
        {
            removed.Update(removed.Order, removed.TitleAr, removed.TitleEn, isHidden: true); // kept for history, never deleted
        }

        var seen = new HashSet<Guid>();
        foreach (var lesson in tree.Lessons)
        {
            if (!sectionKeys.TryGetValue(lesson.SectionId, out var sectionKey))
            {
                continue;
            }

            seen.Add(lesson.Key);
            var section = sections[sectionKey];
            if (lessons.TryGetValue(lesson.Key, out var existing))
            {
                if (existing.SectionId != section.Id)
                {
                    existing.MoveTo(section); // the lesson moved to another section of the blueprint
                }

                existing.Update(lesson.Order, lesson.TitleAr, lesson.TitleEn, lesson.ObjectivesAr, lesson.ObjectivesEn, lesson.PlannedOffsetDays, isHidden: false);
                continue;
            }

            db.Set<ClassLesson>().Add(new ClassLesson(
                section, lesson.Key, lesson.Order, lesson.TitleAr, lesson.TitleEn, lesson.ObjectivesAr, lesson.ObjectivesEn,
                lesson.PlannedOffsetDays));
        }

        foreach (var removed in lessons.Values.Where(l => !seen.Contains(l.Key) && !l.IsHidden))
        {
            removed.Update(removed.Order, removed.TitleAr, removed.TitleEn, removed.ObjectivesAr, removed.ObjectivesEn, removed.PlannedOffsetDays, isHidden: true);
        }

        plan.SyncedTo(tree.Blueprint.Id, tree.Blueprint.Version, clock.UtcNow);
    }

    /// <summary>The teachers who teach the class course today, once per class course; a failure is logged and never undoes a push.</summary>
    private async Task NotifyTeachersAsync(Blueprint blueprint, IReadOnlyList<Guid> classCourseIds, CancellationToken ct)
    {
        if (classCourseIds.Count == 0)
        {
            return;
        }

        try
        {
            var course = await db.Set<Course>().FirstOrDefaultAsync(c => c.Id == blueprint.CourseId, ct);
            if (course is null)
            {
                return;
            }

            var today = DateOnly.FromDateTime(clock.UtcNow);
            foreach (var classCourseId in classCourseIds)
            {
                var teachers = await db.Set<ClassCourseTeacher>()
                    .Where(t => t.ClassCourseId == classCourseId && t.From <= today && (t.To == null || t.To >= today))
                    .Select(t => t.UserId).Distinct().ToListAsync(ct);
                if (teachers.Count == 0)
                {
                    continue;
                }

                var unitPath = await db.Set<ClassCourse>().Where(c => c.Id == classCourseId).Select(c => c.OrgUnitPath).FirstAsync(ct);
                await publisher.PublishAsync(
                    new NotificationRequest(
                        NotificationEvents.BlueprintsPushed, blueprint.OrganizationId, teachers,
                        new Dictionary<string, string>
                        {
                            ["CourseNameAr"] = course.NameAr,
                            ["CourseNameEn"] = course.NameEn,
                            ["Version"] = blueprint.Version.ToString(CultureInfo.InvariantCulture),
                        },
                        OrgUnitPath: unitPath),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The blueprint push notification for blueprint {BlueprintId} could not be published", blueprint.Id);
        }
    }
}
