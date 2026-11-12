using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.IntegrationTests.Courses;

/// <summary>Course supervisors and "My courses" (M3 Task 9, FR-CRS-04, D42).</summary>
[Collection(ApiCollection.Name)]
public sealed class SupervisorTests(LmsApiFactory factory) : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Manager)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            foreach (var unit in new[] { tree.School1, tree.School2 })
            {
                (await db.OrgUnits.SingleAsync(u => u.Id == unit.Id)).SetAcademicFlags(false, null, true);
            }

            await db.SaveChangesAsync();
        }

        return (tree, await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, Guid.NewGuid()));
    }

    private static SaveCourseRequest Request(Guid owner, string name = "Math") =>
        new(AcademicTestKit.NewCode(), "رياضيات " + name, name, null, null, "en", 4, owner, null);

    private async Task<CourseDetailDto> CreateCourseAsync(RoleAssignment who, Guid owner, string name = "Math")
    {
        var response = await _host.SendAsync(HttpMethod.Post, Routes.Courses, who, Request(owner, name));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CourseDetailDto>(Web))!;
    }

    private Task<HttpResponseMessage> SetAsync(RoleAssignment who, Guid courseId, params Guid[] userIds) =>
        _host.SendAsync(HttpMethod.Put, Routes.CourseSupervisors(courseId), who, new SetCourseSupervisorsRequest(userIds));

    private async Task<RoleAssignment> SupervisorAsync(AccessTree tree, OrgUnit unit, Guid? userId = null,
        DateOnly? validTo = null) =>
        await AccessTestData.AssignAsync(factory, tree, unit, BuiltInRoles.Supervisor, userId ?? (await PeopleTestKit.AddUserAsync(factory)).Id, validTo: validTo);

    private static async Task<List<CourseSupervisorDto>> ListAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<List<CourseSupervisorDto>>(Web))!;

    [Fact]
    public async Task Manager_assigns_two_supervisors_and_replaces_them()
    {
        var (tree, manager) = await SetUpAsync("SU1");
        var course = await CreateCourseAsync(manager, tree.School1.Id);
        var atSchool = await SupervisorAsync(tree, tree.School1);
        var atRoot = await SupervisorAsync(tree, tree.Root); // above the owner unit also counts

        var response = await SetAsync(manager, course.Summary.Id, atSchool.UserId, atRoot.UserId, atSchool.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { atSchool.UserId, atRoot.UserId }.Order(), (await ListAsync(response)).Select(s => s.UserId).Order());
        var detail = await (await _host.SendAsync(HttpMethod.Get, Routes.Course(course.Summary.Id), manager)).Content.ReadFromJsonAsync<CourseDetailDto>(Web);
        Assert.Equal(2, detail!.Supervisors.Count);

        var replaced = await SetAsync(manager, course.Summary.Id, atRoot.UserId);
        Assert.Equal([atRoot.UserId], (await ListAsync(replaced)).Select(s => s.UserId));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var rows = await db.Set<CourseSupervisor>().IgnoreQueryFilters().Where(s => s.CourseId == course.Summary.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(r => r.UserId == atSchool.UserId).IsDeleted); // soft-deleted, not erased
        Assert.Empty((await ListAsync(await SetAsync(manager, course.Summary.Id))));
    }

    [Fact]
    public async Task Non_supervisor_user_is_400()
    {
        var (tree, manager) = await SetUpAsync("SU2");
        var course = await CreateCourseAsync(manager, tree.School1.Id);
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var elsewhere = await SupervisorAsync(tree, tree.School2); // a supervisor of the sibling school only
        var expired = await SupervisorAsync(tree, tree.School1, validTo: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));
        var good = await SupervisorAsync(tree, tree.School1);

        foreach (var user in new[] { teacher.UserId, elsewhere.UserId, expired.UserId, Guid.NewGuid() })
        {
            var response = await SetAsync(manager, course.Summary.Id, good.UserId, user);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(ProblemCodes.validation_failed, body.GetProperty("code").GetString());
        }

        Assert.Empty(await ListSupervisorsAsync(manager, course.Summary.Id)); // all-or-nothing
    }

    private async Task<IReadOnlyList<CourseSupervisorDto>> ListSupervisorsAsync(RoleAssignment who, Guid courseId) =>
        (await (await _host.SendAsync(HttpMethod.Get, Routes.Course(courseId), who)).Content.ReadFromJsonAsync<CourseDetailDto>(Web))!.Supervisors;

    [Fact]
    public async Task My_courses_lists_only_supervised_in_active_context()
    {
        var (tree, manager) = await SetUpAsync("SU3");
        var other = await SetUpAsync("SU3B");
        var mine = await CreateCourseAsync(manager, tree.School1.Id, "Mine");
        var notMine = await CreateCourseAsync(manager, tree.School1.Id, "NotMine");
        var supervisor = await SupervisorAsync(tree, tree.School1);
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(manager, mine.Summary.Id, supervisor.UserId)).StatusCode);
        // the same person supervises a course of another client (another context): it must not show here
        var theirAssignment = await SupervisorAsync(other.Tree, other.Tree.School1, supervisor.UserId);
        var foreign = await CreateCourseAsync(other.Manager, other.Tree.School1.Id, "Foreign");
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(other.Manager, foreign.Summary.Id, supervisor.UserId)).StatusCode);

        var list = await (await _host.SendAsync(HttpMethod.Get, Routes.Courses + "?supervisedByMe=true", supervisor))
            .Content.ReadFromJsonAsync<PagedResult<CourseSummaryDto>>(Web);

        Assert.Equal([mine.Summary.Id], list!.Items.Select(c => c.Id));
        var foreignList = await (await _host.SendAsync(HttpMethod.Get, Routes.Courses + "?supervisedByMe=true", theirAssignment))
            .Content.ReadFromJsonAsync<PagedResult<CourseSummaryDto>>(Web);
        Assert.Equal([foreign.Summary.Id], foreignList!.Items.Select(c => c.Id));
        Assert.NotEqual(notMine.Summary.Id, mine.Summary.Id);
    }

    [Fact]
    public async Task Removed_supervisor_loses_course_edit_404()
    {
        var (tree, manager) = await SetUpAsync("SU4");
        var course = await CreateCourseAsync(manager, tree.School1.Id);
        var supervisor = await SupervisorAsync(tree, tree.School1);
        var body = Request(tree.School1.Id, "Renamed");
        // a supervisor who does not supervise the course cannot even see it for editing (D42)
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.Course(course.Summary.Id), supervisor, body)).StatusCode);

        await SetAsync(manager, course.Summary.Id, supervisor.UserId);
        Assert.Equal(HttpStatusCode.OK, (await _host.SendAsync(HttpMethod.Put, Routes.Course(course.Summary.Id), supervisor, body)).StatusCode);

        await SetAsync(manager, course.Summary.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.Course(course.Summary.Id), supervisor, body)).StatusCode);
    }

    [Fact]
    public async Task Supervisor_cannot_manage_supervisors_403()
    {
        var (tree, manager) = await SetUpAsync("SU5");
        var course = await CreateCourseAsync(manager, tree.School1.Id);
        var supervisor = await SupervisorAsync(tree, tree.School1);
        await SetAsync(manager, course.Summary.Id, supervisor.UserId);

        var response = await SetAsync(supervisor, course.Summary.Id, supervisor.UserId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Other_scopes_get_404_and_a_missing_body_is_400()
    {
        var (tree, manager) = await SetUpAsync("SU6");
        var course = await CreateCourseAsync(manager, tree.School1.Id);
        var sibling = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Manager, Guid.NewGuid());
        var outsider = (await SetUpAsync("SU6B")).Manager;

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(sibling, course.Summary.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(outsider, course.Summary.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(manager, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _host.SendAsync(HttpMethod.Put, Routes.CourseSupervisors(course.Summary.Id), manager, new { })).StatusCode);
    }

    [Fact]
    public async Task A_course_owned_above_the_manager_cannot_be_given_supervisors()
    {
        var (tree, manager) = await SetUpAsync("SU7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var rootCourse = await CreateCourseAsync(admin, tree.Root.Id);
        var supervisor = await SupervisorAsync(tree, tree.School1);

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(manager, rootCourse.Summary.Id, supervisor.UserId)).StatusCode); // inherited, not owned
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(admin, rootCourse.Summary.Id, supervisor.UserId)).StatusCode); // supervisor below the owner
    }

    [Fact]
    public async Task A_school_supervisor_sees_a_client_wide_course_in_my_courses()
    {
        var (tree, _) = await SetUpAsync("SU8");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var rootCourse = await CreateCourseAsync(admin, tree.Root.Id, "ClientWide");
        var supervisor = await SupervisorAsync(tree, tree.School1); // assigned below the course's owner unit
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(admin, rootCourse.Summary.Id, supervisor.UserId)).StatusCode);

        var list = await (await _host.SendAsync(HttpMethod.Get, Routes.Courses + "?supervisedByMe=true", supervisor))
            .Content.ReadFromJsonAsync<PagedResult<CourseSummaryDto>>(Web);

        Assert.Equal([rootCourse.Summary.Id], list!.Items.Select(c => c.Id));
    }
}
