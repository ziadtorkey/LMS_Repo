using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Classes.LessonPlanTestKit;

namespace Lms.IntegrationTests.Content;

/// <summary>A class course with a two-lesson plan, its teacher, an enrolled student and the callers around them.</summary>
internal sealed record ContentWorld(
    AccessTree Tree, Class Class, Course Course, ClassCourse ClassCourse, RoleAssignment Admin, RoleAssignment Teacher,
    RoleAssignment OtherTeacher, RoleAssignment Student, Guid StudentProfileId, RoleAssignment Stranger, ClassCourse OtherClassCourse);

internal sealed class ContentTestHost(LmsApiFactory factory) : IDisposable
{
    public static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid(), Lesson2 = Guid.NewGuid();

    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    public async Task<ContentWorld> SetUpAsync(string name)
    {
        var (tree, _, cls, course) = await WorldAsync(factory, name);
        await PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(Section, "Week 1", new TestLesson(Lesson1, "Intro", 0), new TestLesson(Lesson2, "Basics", 7)));
        var classCourse = await AddClassCourseWithPlanAsync(factory, cls, course);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var teacher = await TeacherOfAsync(tree, classCourse);
        var otherClassCourse = await OtherClassCourseAsync(cls, course);
        var otherTeacher = await TeacherOfAsync(tree, otherClassCourse);
        var (student, profile) = await EnrolledStudentAsync(tree, cls, classCourse);
        var stranger = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, (await PeopleTestKit.AddUserAsync(factory)).Id);
        return new ContentWorld(tree, cls, course, classCourse, admin, teacher, otherTeacher, student, profile, stranger, otherClassCourse);
    }

    private async Task<ClassCourse> OtherClassCourseAsync(Class cls, Course course)
    {
        var otherClass = await AcademicTestKit.AddClassAsync(factory, await UnitOfAsync(cls), await YearOfAsync(cls));
        return await AddClassCourseWithPlanAsync(factory, otherClass, course);
    }

    private async Task<Lms.Domain.Organizations.OrgUnit> UnitOfAsync(Class cls)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.OrgUnits.AsNoTracking().SingleAsync(u => u.Id == cls.OrgUnitId);
    }

    private async Task<Lms.Domain.AcademicYears.AcademicYear> YearOfAsync(Class cls)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Lms.Domain.AcademicYears.AcademicYear>().AsNoTracking().SingleAsync(y => y.Id == cls.AcademicYearId);
    }

    private async Task<RoleAssignment> TeacherOfAsync(AccessTree tree, ClassCourse classCourse)
    {
        var user = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, user);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new ClassCourseTeacher(await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id), user, ClassCourseTeacher.PrimaryRole, Today));
        await db.SaveChangesAsync();
        return teacher;
    }

    public async Task<(RoleAssignment Student, Guid ProfileId)> EnrolledStudentAsync(AccessTree tree, Class cls, ClassCourse classCourse)
    {
        var user = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var profile = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(s => s.Id == profile.Id)).LinkUser(user);
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id);
            var placement = new ClassStudent(await db.Set<Class>().SingleAsync(c => c.Id == cls.Id), profile.Id, Today);
            db.AddRange(placement, new CourseEnrollment(cc, placement, Lms.Shared.Enrollment.EnrollmentSources.Auto, Today));
            await db.SaveChangesAsync();
        }

        return (await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, user), profile.Id);
    }

    public async Task<StudentGroup> AddGroupAsync(ClassCourse classCourse, params Guid[] studentIds)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id);
        var group = new StudentGroup(cc, "مجموعة", "Group " + Guid.NewGuid().ToString("N")[..6], null, null);
        db.Add(group);
        foreach (var studentId in studentIds)
        {
            var enrollment = await db.Set<CourseEnrollment>().SingleAsync(e => e.ClassCourseId == cc.Id && e.StudentId == studentId);
            db.Add(new GroupMember(group, enrollment));
        }

        await db.SaveChangesAsync();
        return group;
    }

    public static CreateModuleRequest Page(
        string title = "Page", string visibility = ModuleVisibilities.Published, string rule = CompletionRules.None,
        DateTime? from = null, DateTime? to = null, string target = ModuleTargets.ClassCourse, Guid? groupId = null, string body = "<p>Hello</p>") =>
        new(ModuleKinds.Page, "صفحة " + title, title, visibility, from, to, target, groupId, rule, null, new ModuleContentInput(BodyHtmlEn: body));

    public static CreateModuleRequest Link(string url = "https://example.com/a", string rule = CompletionRules.None) =>
        new(ModuleKinds.Link, "رابط", "Link", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, rule, null,
            new ModuleContentInput(Url: url));

    public async Task<ModuleDetailDto> CreateAsync(RoleAssignment caller, ContentWorld w, CreateModuleRequest request, Guid? lesson = null)
    {
        var response = await SendAsync(caller, HttpMethod.Post, Routes.ClassCourseLessonModules(w.ClassCourse.Id, lesson ?? Lesson1), request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ModuleDetailDto>())!;
    }

    public Task<HttpResponseMessage> CreateRawAsync(RoleAssignment caller, ContentWorld w, CreateModuleRequest request, Guid? lesson = null) =>
        SendAsync(caller, HttpMethod.Post, Routes.ClassCourseLessonModules(w.ClassCourse.Id, lesson ?? Lesson1), request);

    public static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
