using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Lms.Shared.Classes;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Classes.LessonPlanTestKit;

namespace Lms.IntegrationTests.Classes;

/// <summary>M3 Task 17 (#225, FR-LP-03, FR-LP-05, D7): class plans are locked copies of the blueprint; the tree is read-only.</summary>
[Collection(ApiCollection.Name)]
public sealed class ClassLessonPlanTests(LmsApiFactory factory) : IDisposable
{
    private static readonly Guid S1 = Guid.NewGuid(), L1 = Guid.NewGuid(), L2 = Guid.NewGuid();
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private static TestSection[] Tree() =>
        [new(S1, "Week 1", new TestLesson(L1, "Intro", 0), new TestLesson(L2, "Basics", 10))];

    private Task<HttpResponseMessage> GetPlanAsync(RoleAssignment caller, Guid classCourseId) =>
        _host.SendAsync(HttpMethod.Get, Routes.ClassCourseLessonPlan(classCourseId), caller);

    private static async Task<ClassLessonPlanDto> PlanDtoAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ClassLessonPlanDto>())!;
    }

    [Fact]
    public async Task New_class_course_gets_copy_of_published_blueprint()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "LP1");
        var blueprint = await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, Tree());

        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);

        var (sections, lessons, plan) = await PlanOfAsync(factory, classCourse.Id);
        Assert.Equal((blueprint.Id, 1), (plan.BlueprintId, plan.BlueprintVersion));
        Assert.NotNull(plan.SyncedAt);
        Assert.Equal([S1], sections.Select(s => s.Key).ToArray());
        Assert.Equal([L1, L2], lessons.Select(l => l.Key).ToArray());
        Assert.Equal([0, 10], lessons.Select(l => l.PlannedOffsetDays).ToArray());
        Assert.Equal(sections[0].Id, lessons[0].SectionId);
        Assert.All(lessons, l => Assert.False(l.IsHidden));
        Assert.Equal(cls.OrgUnitId, plan.OrgUnitId); // the plan lives at the class unit, not at the course's owner unit
    }

    [Fact]
    public async Task Class_course_of_a_course_without_blueprint_gets_an_empty_plan_and_removing_it_removes_the_plan()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "LP2");

        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);

        var (sections, lessons, plan) = await PlanOfAsync(factory, classCourse.Id);
        Assert.Empty(sections);
        Assert.Empty(lessons);
        Assert.Null(plan.BlueprintVersion);

        await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, Tree());
        var filled = await AddClassCourseWithPlanAsync(factory, await AcademicTestKit.AddClassAsync(
            factory, await UnitAsync(cls), await YearAsync(cls)), course);
        using (var scope = new PlanScope(factory.Services))
        {
            var tracked = await scope.Db.Set<ClassCourse>().SingleAsync(c => c.Id == filled.Id);
            await scope.Sync.OnRemovingAsync(tracked, default);
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<ClassLessonPlan>().AnyAsync(p => p.ClassCourseId == filled.Id));
        Assert.Equal(0, await db.Set<ClassLesson>().CountAsync(l => l.OrgUnitId == cls.OrgUnitId && l.Key == L1 && db.Set<ClassLessonPlan>().IgnoreQueryFilters().Any(p => p.ClassCourseId == filled.Id && p.Id == l.PlanId && !p.IsDeleted)));
    }

    private async Task<Lms.Domain.Organizations.OrgUnit> UnitAsync(Class cls)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.OrgUnits.SingleAsync(u => u.Id == cls.OrgUnitId);
    }

    private async Task<Lms.Domain.AcademicYears.AcademicYear> YearAsync(Class cls)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Lms.Domain.AcademicYears.AcademicYear>().SingleAsync(y => y.Id == cls.AcademicYearId);
    }

    [Fact]
    public async Task Plan_tree_is_read_only_dated_from_the_year_start_and_labelled()
    {
        var (tree, year, cls, course) = await WorldAsync(factory, "LP3");
        await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, Tree());
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var dto = await PlanDtoAsync(await GetPlanAsync(admin, classCourse.Id));

        Assert.Equal((classCourse.Id, 1, "Week"), (dto.ClassCourseId, dto.BlueprintVersion, dto.SectionLabel));
        var section = Assert.Single(dto.Sections);
        Assert.Equal(["Intro", "Basics"], section.Lessons.Select(l => l.TitleEn).ToArray());
        Assert.Equal([year.StartDate, year.StartDate.AddDays(10)], section.Lessons.Select(l => l.PlannedDate).ToArray());

        // Locked: there is no way to change the copy through the API (only GET exists on the route).
        var write = await _host.SendAsync(HttpMethod.Put, Routes.ClassCourseLessonPlan(classCourse.Id), admin, new { });
        Assert.True(write.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Student_sees_plan_without_hidden_lessons_and_teacher_of_other_class_gets_404()
    {
        var (tree, _, cls, course) = await WorldAsync(factory, "LP4");
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting, Tree());
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);
        // v2 drops lesson 2: it stays in the plan as hidden history.
        await RunPushAsync(factory, await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(S1, "Week 1", new TestLesson(L1, "Intro", 0))));

        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var studentUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var studentProfile = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == studentProfile.Id)).LinkUser(studentUser);
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id);
            var placement = new ClassStudent(await db.Set<Class>().SingleAsync(c => c.Id == cls.Id), studentProfile.Id, Today);
            db.AddRange(placement, new CourseEnrollment(cc, placement, EnrollmentSources.Auto, Today));
            await db.SaveChangesAsync();
        }

        var student = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, studentUser);
        var otherClass = await AcademicTestKit.AddClassAsync(factory, await UnitAsync(cls), await YearAsync(cls));
        var otherCourse = await AddClassCourseWithPlanAsync(factory, otherClass, course);
        var teacherUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacherUser);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(await db.Set<ClassCourse>().SingleAsync(c => c.Id == otherCourse.Id), teacherUser, ClassCourseTeacher.PrimaryRole, Today));
            await db.SaveChangesAsync();
        }

        var asStaff = await PlanDtoAsync(await GetPlanAsync(admin, classCourse.Id));
        var asStudent = await PlanDtoAsync(await GetPlanAsync(student, classCourse.Id));

        Assert.Equal([L1, L2], asStaff.Sections.Single().Lessons.Select(l => l.Key).ToArray());
        Assert.True(asStaff.Sections.Single().Lessons.Single(l => l.Key == L2).IsHidden); // staff keep the history
        Assert.Equal([L1], asStudent.Sections.Single().Lessons.Select(l => l.Key).ToArray()); // the student never sees it
        Assert.Equal(HttpStatusCode.NotFound, (await GetPlanAsync(teacher, classCourse.Id)).StatusCode); // another class's teacher
        Assert.Equal(HttpStatusCode.OK, (await GetPlanAsync(teacher, otherCourse.Id)).StatusCode); // their own
        var stranger = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, (await PeopleTestKit.AddUserAsync(factory)).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await GetPlanAsync(stranger, classCourse.Id)).StatusCode); // a student not enrolled
    }

    [Fact]
    public async Task Backfill_creates_missing_plans_once()
    {
        var (_, _, cls, course) = await WorldAsync(factory, "LP5");
        await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, Tree());
        ClassCourse missing;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            missing = new ClassCourse(await db.Set<Class>().SingleAsync(c => c.Id == cls.Id), course, false); // no handler ran
            db.Add(missing);
            await db.SaveChangesAsync();
        }

        int first;
        int second;
        using (var scope = new PlanScope(factory.Services))
        {
            first = await scope.Backfill.RunAsync(default);
        }

        using (var scope = new PlanScope(factory.Services))
        {
            second = await scope.Backfill.RunAsync(default);
        }

        Assert.True(first >= 1);
        Assert.Equal(0, second); // idempotent
        var (sections, lessons, plan) = await PlanOfAsync(factory, missing.Id);
        Assert.Equal(1, plan.BlueprintVersion);
        Assert.Single(sections);
        Assert.Equal(2, lessons.Count);
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await read.Set<ClassLessonPlan>().CountAsync(p => p.ClassCourseId == missing.Id)); // exactly one
    }

    [Fact]
    public async Task Plan_rows_are_isolated_and_the_plan_is_audited()
    {
        var (tree, _, cls, course) = await WorldAsync(factory, "LP6");
        var other = await WorldAsync(factory, "LP6B");
        await PublishAsync(factory, course, BlueprintApplyModes.NewClassesOnly, Tree());
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);
        var otherAdmin = await AccessTestData.AssignAsync(factory, other.Tree, other.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Admin = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var (_, _, plan) = await PlanOfAsync(factory, classCourse.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await GetPlanAsync(otherAdmin, classCourse.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetPlanAsync(school2Admin, classCourse.Id)).StatusCode);
        await using var asOther = factory.CreateDbContext(TestCurrentContext.For(other.Tree.Org.Id, other.Tree.Root.Path));
        Assert.False(await asOther.Set<ClassLessonPlan>().AnyAsync(p => p.Id == plan.Id));
        Assert.False(await asOther.Set<ClassSection>().AnyAsync(s => s.PlanId == plan.Id));
        Assert.False(await asOther.Set<ClassLesson>().AnyAsync(l => l.PlanId == plan.Id));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(ClassLessonPlan) && e.EntityId == plan.Id && e.Action == AuditAction.Created));
    }
}
