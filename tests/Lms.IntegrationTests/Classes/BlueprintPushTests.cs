using Lms.Domain.AcademicYears;
using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Domain.Notifications;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Blueprints;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Classes.LessonPlanTestKit;

namespace Lms.IntegrationTests.Classes;

/// <summary>M3 Task 17 (#225, FR-LP-04, Review Focus 4): publishing a blueprint version and pushing it into the class plans.</summary>
[Collection(ApiCollection.Name)]
public sealed class BlueprintPushTests(LmsApiFactory factory)
{
    private static readonly Guid S1 = Guid.NewGuid(), S2 = Guid.NewGuid(), L1 = Guid.NewGuid(), L2 = Guid.NewGuid(), L3 = Guid.NewGuid();

    private static TestSection[] V1() =>
        [new(S1, "Week 1", new TestLesson(L1, "Intro", 0), new TestLesson(L2, "Basics", 7))];

    [Fact]
    public async Task Push_adds_new_lesson_updates_renamed_and_hides_removed()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "BP1");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, V1());
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);

        // v2: lesson 1 renamed and rescheduled, lesson 2 removed, lesson 3 and a whole new section added.
        var v2 = await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(S1, "Week one", new TestLesson(L1, "Introduction", 1)),
            new TestSection(S2, "Week 2", new TestLesson(L3, "Advanced", 14)));
        await RunPushAsync(factory, v2);

        var (sections, lessons, plan) = await PlanOfAsync(factory, classCourse.Id);
        Assert.Equal((v2.Id, 2), (plan.BlueprintId, plan.BlueprintVersion));
        Assert.Equal(["Week one", "Week 2"], sections.Select(s => s.TitleEn).ToArray());
        Assert.All(sections, s => Assert.False(s.IsHidden));
        var l1 = lessons.Single(l => l.Key == L1);
        Assert.Equal(("Introduction", 1, false), (l1.TitleEn, l1.PlannedOffsetDays, l1.IsHidden));
        var l2 = lessons.Single(l => l.Key == L2);
        Assert.True(l2.IsHidden); // removed from the blueprint: kept for history, never deleted
        Assert.Equal("Basics", l2.TitleEn);
        var l3 = lessons.Single(l => l.Key == L3);
        Assert.Equal((sections[1].Id, 14, false), (l3.SectionId, l3.PlannedOffsetDays, l3.IsHidden));
    }

    [Fact]
    public async Task Removed_then_restored_lesson_is_unhidden()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "BP2");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, V1());
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);
        await RunPushAsync(factory, await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, new TestSection(S1, "Week 1", new TestLesson(L1, "Intro"))));
        Assert.True((await PlanOfAsync(factory, classCourse.Id)).Lessons.Single(l => l.Key == L2).IsHidden);

        await RunPushAsync(factory, await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(S1, "Week 1", new TestLesson(L1, "Intro"), new TestLesson(L2, "Basics again", 3))));

        var restored = (await PlanOfAsync(factory, classCourse.Id)).Lessons.Single(l => l.Key == L2);
        Assert.Equal((false, "Basics again", 3), (restored.IsHidden, restored.TitleEn, restored.PlannedOffsetDays));
    }

    [Fact]
    public async Task A_lesson_that_moves_to_another_section_follows_it()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "BP3");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, new TestSection(S1, "Week 1", new TestLesson(L1, "Intro", 0)), new TestSection(S2, "Week 2", new TestLesson(L3, "Later")));
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);

        await RunPushAsync(factory, await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(S1, "Week 1", new TestLesson(L1, "Intro")),
            new TestSection(S2, "Week 2", new TestLesson(L3, "Later"), new TestLesson(L2, "Basics"))));

        var (sections, lessons, _) = await PlanOfAsync(factory, classCourse.Id);
        Assert.Equal(sections.Single(s => s.Key == S2).Id, lessons.Single(l => l.Key == L2).SectionId);
        Assert.False(lessons.Single(l => l.Key == L2).IsHidden);
    }

    [Fact]
    public async Task NewClassesOnly_publish_leaves_existing_plans_but_fills_empty_ones()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "BP4");
        var empty = await AddClassCourseWithPlanAsync(factory, cls, course); // no blueprint yet: an empty plan
        Assert.Null((await PlanOfAsync(factory, empty.Id)).Plan.BlueprintVersion);
        await RunPushAsync(factory, await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, V1())); // fills the empty one
        var otherClass = await AcademicTestKit.AddClassAsync(factory, (await CountsAsync(cls)).Unit, (await CountsAsync(cls)).Year);
        var existing = await AddClassCourseWithPlanAsync(factory, otherClass, course); // copies v1 on creation

        var v2 = await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly,
            new TestSection(S1, "Changed", new TestLesson(L1, "Intro changed"), new TestLesson(L3, "Added")));
        var emptyAgain = await AddClassCourseWithPlanAsync(factory, await AcademicTestKit.AddClassAsync(factory, (await CountsAsync(cls)).Unit, (await CountsAsync(cls)).Year), course);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // Simulate a plan that never received a version (created while the blueprint could not be read).
            var plan = await db.Set<ClassLessonPlan>().SingleAsync(p => p.ClassCourseId == emptyAgain.Id);
            db.Entry(plan).Property(nameof(ClassLessonPlan.BlueprintVersion)).CurrentValue = null;
            await db.SaveChangesAsync();
        }

        await RunPushAsync(factory, v2);

        Assert.Equal(1, (await PlanOfAsync(factory, existing.Id)).Plan.BlueprintVersion); // left alone
        Assert.Equal(1, (await PlanOfAsync(factory, empty.Id)).Plan.BlueprintVersion); // already filled by v1
        Assert.Equal("Intro", (await PlanOfAsync(factory, existing.Id)).Lessons.Single(l => l.Key == L1).TitleEn);
        var filled = await PlanOfAsync(factory, emptyAgain.Id);
        Assert.Equal(2, filled.Plan.BlueprintVersion); // the empty one got the new version
        Assert.Contains(filled.Lessons, l => l.Key == L3);
    }

    private async Task<(Lms.Domain.Organizations.OrgUnit Unit, AcademicYear Year)> CountsAsync(Class cls)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return (await db.OrgUnits.SingleAsync(u => u.Id == cls.OrgUnitId), await db.Set<AcademicYear>().SingleAsync(y => y.Id == cls.AcademicYearId));
    }

    [Fact]
    public async Task Push_skips_closed_year_and_closed_class()
    {
        var (tree, year, cls, course) = await WorldAsync(factory, "BP5");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, V1());
        var inClosedClass = await AddClassCourseWithPlanAsync(factory, cls, course);
        var otherYear = await AcademicTestKit.AddYearAsync(factory, tree.School2);
        var classInClosedYear = await AcademicTestKit.AddClassAsync(factory, tree.School2, otherYear);
        var inClosedYear = await AddClassCourseWithPlanAsync(factory, classInClosedYear, course);
        var openClass = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var open = await AddClassCourseWithPlanAsync(factory, openClass, course);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Class>().SingleAsync(c => c.Id == cls.Id)).SetStatus(Class.ClosedStatus);
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == otherYear.Id)).Close();
            await db.SaveChangesAsync();
        }

        await RunPushAsync(factory, await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(S1, "Week 1", new TestLesson(L1, "Intro"), new TestLesson(L3, "New"))));

        Assert.Equal(1, (await PlanOfAsync(factory, inClosedClass.Id)).Plan.BlueprintVersion);
        Assert.Equal(1, (await PlanOfAsync(factory, inClosedYear.Id)).Plan.BlueprintVersion);
        Assert.Equal(2, (await PlanOfAsync(factory, open.Id)).Plan.BlueprintVersion);
    }

    [Fact]
    public async Task Running_push_twice_changes_nothing_and_teachers_are_notified_once()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "BP6");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, V1());
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);
        var teacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id), teacher, ClassCourseTeacher.PrimaryRole, Today));
            await db.SaveChangesAsync();
        }

        var v2 = await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(S1, "Week 1", new TestLesson(L1, "Intro"), new TestLesson(L3, "New")));
        await RunPushAsync(factory, v2);
        var first = await PlanOfAsync(factory, classCourse.Id);

        await RunPushAsync(factory, v2);
        await RunPushAsync(factory, v2);

        var second = await PlanOfAsync(factory, classCourse.Id);
        Assert.Equal(first.Plan.SyncedAt, second.Plan.SyncedAt);
        Assert.Equal(first.Lessons.Select(l => l.Id).Order(), second.Lessons.Select(l => l.Id).Order());
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var notifications = await read.Set<Notification>().Where(n => n.UserId == teacher && n.EventKey == NotificationEvents.BlueprintsPushed).ToListAsync();
        Assert.Single(notifications); // once, not once per run
    }

    [Fact]
    public async Task Publish_hook_queues_the_push_and_reports_how_many_plans()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "BP7");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, V1());
        await AddClassCourseWithPlanAsync(factory, cls, course);
        await AddClassCourseWithPlanAsync(factory, await AcademicTestKit.AddClassAsync(factory, (await CountsAsync(cls)).Unit, (await CountsAsync(cls)).Year), course);
        var v2 = await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, V1());
        var v3 = await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, V1());

        using var scope = new PlanScope(factory.Services);
        var all = await scope.Sync.OnPublishedAsync(await scope.Db.Set<Blueprint>().SingleAsync(b => b.Id == v2.Id), default);
        var onlyNew = await scope.Sync.OnPublishedAsync(await scope.Db.Set<Blueprint>().SingleAsync(b => b.Id == v3.Id), default);

        Assert.Equal(2, all); // PushToExisting touches every plan of the course
        Assert.Equal(0, onlyNew); // NewClassesOnly: every plan already has a version
    }
}
