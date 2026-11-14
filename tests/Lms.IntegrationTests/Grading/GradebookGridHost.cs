using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Grading;

/// <summary>A class course with enrolled students, its teacher, another teacher, a supervisor and an admin, and helpers for the gradebook endpoints (M5 Task 8).</summary>
internal sealed class GradebookGridHost(LmsApiFactory factory) : IDisposable
{
    private static readonly DateOnly Past = new(2020, 1, 1);

    public OrgUnitTestHost Host { get; } = new(factory);

    public void Dispose() => Host.Dispose();

    public sealed record World(
        AccessTree Tree, GradebookTestKit.World Gb, IReadOnlyList<StudentProfile> Students, RoleAssignment Admin, RoleAssignment Teacher,
        RoleAssignment OtherTeacher, RoleAssignment Supervisor);

    public Guid ClassCourseId(World w) => w.Gb.ClassCourse.Id;

    public async Task<World> SeedAsync(string name, int students = 3)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var gb = await GradebookTestKit.NewWorldAsync(factory, tree.School1);
        var teacherUser = await PeopleTestKit.AddUserAsync(factory);
        var supervisorUser = await PeopleTestKit.AddUserAsync(factory);
        var profiles = new List<StudentProfile>();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == gb.ClassCourse.Id);
            db.Add(new ClassCourseTeacher(cc, teacherUser.Id, ClassCourseTeacher.PrimaryRole, Past));
            var course = await db.Set<Lms.Domain.Courses.Course>().SingleAsync(c => c.Id == cc.CourseId);
            db.Add(new Lms.Domain.Courses.CourseSupervisor(course, supervisorUser.Id));
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < students; i++)
        {
            profiles.Add(await EnrollNewStudentAsync(tree, gb));
        }

        return new World(
            tree, gb, profiles,
            await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacherUser.Id),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Supervisor, supervisorUser.Id));
    }

    public async Task<StudentProfile> EnrollNewStudentAsync(AccessTree tree, GradebookTestKit.World gb)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        var placement = await AcademicTestKit.PlaceAsync(factory, gb.Class, student);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == gb.ClassCourse.Id);
        db.Add(new CourseEnrollment(cc, placement, "Manual", Past));
        await db.SaveChangesAsync();
        return student;
    }

    public Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        Host.SendAsync(method, url, caller, body);

    public string Url(World w, string? suffix = null, Guid? term = null, string? query = null)
    {
        var url = Routes.ClassCourseGradebook(ClassCourseId(w)) + suffix;
        var q = new List<string>();
        if (term is { } t)
        {
            q.Add("termId=" + t);
        }

        if (query is not null)
        {
            q.Add(query);
        }

        return q.Count == 0 ? url : url + "?" + string.Join('&', q);
    }

    public async Task<GradebookDto> GridAsync(World w, RoleAssignment? caller = null, string? query = null)
    {
        var response = await SendAsync(caller ?? w.Admin, HttpMethod.Get, Url(w, term: w.Gb.First.Id, query: query));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GradebookDto>())!;
    }

    public async Task<GradeItemDto> AddItemAsync(World w, string title = "Paper test", decimal max = 20, Guid? category = null)
    {
        var grid = await GridAsync(w);
        var response = await SendAsync(w.Admin, HttpMethod.Post, Url(w, "/items", w.Gb.First.Id),
            new CreateGradeItemRequest(category ?? grid.Categories.Single(c => c.IsUncategorised).Id, "ورقة " + title, title, max));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GradeItemDto>())!;
    }

    public async Task<HttpResponseMessage> PutEntriesAsync(World w, RoleAssignment caller, Guid student, long version, params GradeEntryChange[] changes) =>
        await SendAsync(caller, HttpMethod.Put, Url(w, "/entries", w.Gb.First.Id), new UpsertEntriesRequest(student, version, changes));

    public async Task<GradeRowDto> EntriesAsync(World w, Guid student, long version, params GradeEntryChange[] changes)
    {
        var response = await PutEntriesAsync(w, w.Admin, student, version, changes);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GradeRowDto>())!;
    }

    public async Task RecordEventAsync(World w, Guid student, Guid source, decimal points, int revision, DateTime at, decimal max = 10)
    {
        using var scope = new EngineScope(factory.Services);
        await scope.Services.GetRequiredService<IGradeEvents>().RecordAsync(
            new GradeEventInput(GradeSourceKind.Assignment, source, w.Gb.ClassCourse.Id, student, points, max, at, revision), default);
        await scope.Db.SaveChangesAsync();
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    public static GradeCellDto Cell(GradeRowDto row, Guid itemId) => row.Cells.Single(c => c.ItemId == itemId);
}
