using Lms.Api.Infrastructure;
using Lms.Domain.AcademicYears;
using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Infrastructure.Classes.LessonPlans;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Classes;

/// <summary>One lesson of a test blueprint: a stable key, a title and an optional offset from the year start.</summary>
internal sealed record TestLesson(Guid Key, string Title, int? Offset = null);

internal sealed record TestSection(Guid Key, string Title, params TestLesson[] Lessons);

/// <summary>A request scope with the platform context (a system job): the sync, the push job and the backfill.</summary>
internal sealed class PlanScope : IDisposable
{
    private readonly IServiceScope _scope;

    public PlanScope(IServiceProvider services)
    {
        _scope = services.CreateScope();
        _scope.ServiceProvider.GetRequiredService<HttpCurrentContext>().SetUser(Guid.Empty, true);
    }

    public ClassLessonPlanSync Sync => _scope.ServiceProvider.GetRequiredService<ClassLessonPlanSync>();

    public BlueprintPushJob Push => _scope.ServiceProvider.GetRequiredService<BlueprintPushJob>();

    public LessonPlanBackfill Backfill => _scope.ServiceProvider.GetRequiredService<LessonPlanBackfill>();

    public Lms.Infrastructure.Persistence.LmsDbContext Db => _scope.ServiceProvider.GetRequiredService<Lms.Infrastructure.Persistence.LmsDbContext>();

    public void Dispose() => _scope.Dispose();
}

internal static class LessonPlanTestKit
{
    public static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Publishes the next version of the course's blueprint with the given tree; the previous published one is superseded.</summary>
    public static async Task<Blueprint> PublishAsync(
        LmsApiFactory factory, Course course, string mode, params TestSection[] sections)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<Course>().SingleAsync(c => c.Id == course.Id);
        var versions = await db.Set<Blueprint>().Where(b => b.CourseId == course.Id && b.Status != Blueprint.DraftStatus).ToListAsync();
        foreach (var previous in versions.Where(b => b.Status == Blueprint.PublishedStatus))
        {
            previous.Supersede();
        }

        var blueprint = Blueprint.CreateDraft(tracked);
        db.Add(blueprint);
        var order = 0;
        foreach (var section in sections)
        {
            var s = new BlueprintSection(blueprint, section.Key, order++, "قسم " + section.Title, section.Title);
            db.Add(s);
            var lessonOrder = 0;
            foreach (var lesson in section.Lessons)
            {
                db.Add(new BlueprintLesson(s, lesson.Key, lessonOrder++, "درس " + lesson.Title, lesson.Title, null, "Objectives of " + lesson.Title, lesson.Offset));
            }
        }

        blueprint.Publish(versions.Count == 0 ? 1 : versions.Max(b => b.Version) + 1, mode, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        return blueprint;
    }

    public static async Task<ClassCourse> AddClassCourseWithPlanAsync(LmsApiFactory factory, Class cls, Course course, bool optional = false)
    {
        ClassCourse classCourse;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            classCourse = new ClassCourse(await db.Set<Class>().SingleAsync(c => c.Id == cls.Id), course, optional);
            db.Add(classCourse);
            await db.SaveChangesAsync();
        }

        using var scope = new PlanScope(factory.Services);
        await scope.Sync.OnAddedAsync(await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id), default);
        return classCourse;
    }

    public static async Task<(List<ClassSection> Sections, List<ClassLesson> Lessons, ClassLessonPlan Plan)> PlanOfAsync(
        LmsApiFactory factory, Guid classCourseId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var plan = await db.Set<ClassLessonPlan>().AsNoTracking().SingleAsync(p => p.ClassCourseId == classCourseId);
        return (await db.Set<ClassSection>().AsNoTracking().Where(s => s.PlanId == plan.Id).OrderBy(s => s.Order).ToListAsync(),
            await db.Set<ClassLesson>().AsNoTracking().Where(l => l.PlanId == plan.Id).OrderBy(l => l.Order).ToListAsync(), plan);
    }

    public static async Task RunPushAsync(LmsApiFactory factory, Blueprint blueprint)
    {
        using var scope = new PlanScope(factory.Services);
        await scope.Push.RunAsync(blueprint.Id, default);
    }

    /// <summary>A year at the root, Active, with a class at School1 (and a second year plus class when asked).</summary>
    public static async Task<(AccessTree Tree, AcademicYear Year, Class Class, Course Course)> WorldAsync(LmsApiFactory factory, string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == year.Id)).Activate();
            await db.SaveChangesAsync();
        }

        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.Root); // owned above the class unit
        return (tree, year, cls, course);
    }
}
