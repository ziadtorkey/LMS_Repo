using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.Files;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;

namespace Lms.IntegrationTests.Forums;

/// <summary>
/// One school with a class of two class courses: CcA (teacher A, student A) and CcB (teacher B, student B), plus an admin at the
/// root. Students are placed and enrolled only in their own class course.
/// </summary>
internal sealed class ForumTestWorld : IDisposable
{
    private readonly LmsApiFactory _factory;
    private readonly OrgUnitTestHost _host;

    private ForumTestWorld(LmsApiFactory factory, OrgUnitTestHost host, EnrollmentWorld world)
    {
        _factory = factory;
        _host = host;
        World = world;
    }

    public EnrollmentWorld World { get; }
    public RoleAssignment Admin { get; private set; } = null!;
    public RoleAssignment TeacherA { get; private set; } = null!;
    public RoleAssignment TeacherB { get; private set; } = null!;
    public RoleAssignment StudentA { get; private set; } = null!;
    public RoleAssignment StudentB { get; private set; } = null!;
    public RoleAssignment StudentA2 { get; private set; } = null!;
    public Guid ClassCourseA => World.CcA.Id;
    public Guid ClassCourseB => World.CcB.Id;

    public static async Task<ForumTestWorld> CreateAsync(LmsApiFactory factory, string name)
    {
        var world = await EnrollmentWorld.CreateAsync(factory, name);
        var me = new ForumTestWorld(factory, new OrgUnitTestHost(factory), world);
        await me.SeedPeopleAsync();
        return me;
    }

    public void Dispose() => _host.Dispose();

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    public async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>An asset of the given class course, stored directly (the upload path is tested with the asset service).</summary>
    public Task<Guid> AddAssetAsync(ClassCourse classCourse) => AddAssetAsync(_factory, classCourse);

    public static async Task<Guid> AddAssetAsync(LmsApiFactory factory, ClassCourse classCourse)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var file = new StoredFile(classCourse.OrganizationId, classCourse.OrgUnitId, classCourse.OrgUnitPath, "n.pdf", "application/pdf", 1, DateTime.UtcNow);
        var asset = new Asset(classCourse, file.Id, AssetKinds.Document, "n.pdf", "application/pdf", 1);
        db.AddRange(file, asset);
        await db.SaveChangesAsync();
        return asset.Id;
    }

    public Guid UserIdOf(RoleAssignment assignment) => assignment.UserId;

    private async Task SeedPeopleAsync()
    {
        var tree = World.Tree;
        var admin = await AccessTestData.AssignAsync(_factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacherAUser = await PeopleTestKit.AddUserAsync(_factory);
        var teacherBUser = await PeopleTestKit.AddUserAsync(_factory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (studentA, userA) = await StudentAsync("FA");
        var (studentA2, userA2) = await StudentAsync("FA2");
        var (studentB, userB) = await StudentAsync("FB");

        await using (var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var placementA = new ClassStudent(World.Class, studentA.Id, today.AddDays(-5));
            var placementA2 = new ClassStudent(World.Class, studentA2.Id, today.AddDays(-5));
            var placementB = new ClassStudent(World.Class, studentB.Id, today.AddDays(-5));
            db.AddRange(
                placementA, placementA2, placementB,
                new CourseEnrollment(World.CcA, placementA, "Auto", today.AddDays(-5)),
                new CourseEnrollment(World.CcA, placementA2, "Auto", today.AddDays(-5)),
                new CourseEnrollment(World.CcB, placementB, "Auto", today.AddDays(-5)),
                new ClassCourseTeacher(World.CcA, teacherAUser.Id, ClassCourseTeacher.PrimaryRole, today.AddDays(-5)),
                new ClassCourseTeacher(World.CcB, teacherBUser.Id, ClassCourseTeacher.PrimaryRole, today.AddDays(-5)));
            await db.SaveChangesAsync();
        }

        Admin = admin;
        TeacherA = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Teacher, teacherAUser.Id);
        TeacherB = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Teacher, teacherBUser.Id);
        StudentA = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Student, userA.Id);
        StudentA2 = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Student, userA2.Id);
        StudentB = await AccessTestData.AssignAsync(_factory, tree, tree.School1, BuiltInRoles.Student, userB.Id);
    }

    private async Task<(StudentProfile Student, Lms.Domain.Identity.User User)> StudentAsync(string tag)
    {
        var student = await PeopleTestKit.AddStudentAsync(_factory, World.Tree.School1, PeopleTestKit.NewCode(), "طالب " + tag, "Student " + tag);
        var user = await PeopleTestKit.AddUserAsync(_factory);
        await using var db = _factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<StudentProfile>(), s => s.Id == student.Id)).LinkUser(user.Id);
        await db.SaveChangesAsync();
        return (student, user);
    }
}
