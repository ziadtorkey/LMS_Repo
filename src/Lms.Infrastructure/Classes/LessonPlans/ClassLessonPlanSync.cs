using Lms.Domain.Abstractions;
using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Classes.LessonPlans;

/// <summary>
/// Keeps class lesson plans in step with blueprints (FR-LP-03, FR-LP-04, D7). A class course gets a copy of the course's
/// latest published blueprint when it is added (or an empty plan when there is none yet); when a version is published the
/// push job merges it into the existing plans, as the apply mode asks. Plans are locked: nothing but this module writes them.
/// </summary>
public sealed class ClassLessonPlanSync(LmsDbContext db, LessonPlanReads reads, IClock clock, IBackgroundJobs jobs)
    : IClassCourseLifecycleHandler, IBlueprintPublished
{
    public async Task OnAddedAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        // Idempotent: a plan that already exists (the start-up backfill, a retry) is left as it is.
        if (await db.Set<ClassLessonPlan>().AnyAsync(p => p.ClassCourseId == classCourse.Id, cancellationToken))
        {
            return;
        }

        await CreatePlanAsync(classCourse, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task OnRemovingAsync(ClassCourse classCourse, CancellationToken cancellationToken)
    {
        // The plan goes with the class course (soft delete frees its unique key; the sections and lessons are removed).
        var plan = await db.Set<ClassLessonPlan>().FirstOrDefaultAsync(p => p.ClassCourseId == classCourse.Id, cancellationToken);
        if (plan is null)
        {
            return;
        }

        var lessons = await db.Set<ClassLesson>().Where(l => l.PlanId == plan.Id).ToListAsync(cancellationToken);
        var sections = await db.Set<ClassSection>().Where(s => s.PlanId == plan.Id).ToListAsync(cancellationToken);
        db.Set<ClassLesson>().RemoveRange(lessons);
        db.Set<ClassSection>().RemoveRange(sections);
        db.Set<ClassLessonPlan>().Remove(plan);
        await db.SaveChangesAsync(cancellationToken);

        // Saved and detached: a tracked dependent would make EF try to sever it when the caller removes the class course.
        foreach (var tracked in lessons.Cast<object>().Concat(sections).Append(plan))
        {
            db.Entry(tracked).State = EntityState.Detached;
        }
    }

    public Task OnOptionalChangedAsync(ClassCourse classCourse, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Adds (without saving) the plan of a class course, copied from the course's latest published blueprint.</summary>
    public async Task<ClassLessonPlan> CreatePlanAsync(ClassCourse classCourse, CancellationToken ct)
    {
        var tree = await reads.LatestPublishedAsync(classCourse.OrganizationId, classCourse.CourseId, ct);
        var plan = new ClassLessonPlan(
            classCourse, tree?.Blueprint.Id, tree?.Blueprint.Version, tree is null ? null : clock.UtcNow);
        db.Set<ClassLessonPlan>().Add(plan);
        if (tree is null)
        {
            return plan;
        }

        var sections = new Dictionary<Guid, ClassSection>();
        foreach (var section in tree.Sections)
        {
            var copy = new ClassSection(plan, section.Key, section.Order, section.TitleAr, section.TitleEn);
            sections[section.Id] = copy;
            db.Set<ClassSection>().Add(copy);
        }

        foreach (var lesson in tree.Lessons.Where(l => sections.ContainsKey(l.SectionId)))
        {
            db.Set<ClassLesson>().Add(new ClassLesson(
                sections[lesson.SectionId], lesson.Key, lesson.Order, lesson.TitleAr, lesson.TitleEn, lesson.ObjectivesAr,
                lesson.ObjectivesEn, lesson.PlannedOffsetDays));
        }

        return plan;
    }

    /// <summary>
    /// After a blueprint was published: queues the push job and returns how many class plans it will touch. Both apply modes
    /// queue the job (NewClassesOnly still fills the plans that have no version yet); the job reads the mode.
    /// </summary>
    public async Task<int> OnPublishedAsync(Blueprint blueprint, CancellationToken cancellationToken)
    {
        var mode = blueprint.ApplyMode;
        // ISOLATION-BYPASS: the plans of the course are in classes anywhere in the client, not only in the publisher's scope.
        var plans = db.Set<ClassLessonPlan>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == blueprint.OrganizationId && !p.IsDeleted
                        && db.Set<ClassCourse>().IgnoreQueryFilters().Any(c => c.Id == p.ClassCourseId && c.CourseId == blueprint.CourseId));
        var queued = mode == BlueprintApplyModes.NewClassesOnly
            ? await plans.CountAsync(p => p.BlueprintVersion == null, cancellationToken)
            : await plans.CountAsync(cancellationToken);
        if (queued == 0)
        {
            return 0;
        }

        // A system job: it must see every class of the client, whatever scope the publisher has.
        jobs.EnqueueAsSystem<BlueprintPushJob>(j => j.RunAsync(blueprint.Id, CancellationToken.None));
        return queued;
    }
}
