using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
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

namespace Lms.IntegrationTests.Courses;

/// <summary>Course allocations in the tree (M3 Task 7, FR-CRS-02).</summary>
[Collection(ApiCollection.Name)]
public sealed class AllocationTests(LmsApiFactory factory) : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        foreach (var id in new[] { tree.School1.Id, tree.School2.Id })
        {
            (await db.OrgUnits.SingleAsync(u => u.Id == id)).SetAcademicFlags(false, null, true);
        }

        await db.SaveChangesAsync();
        return (tree, admin);
    }

    private Task<HttpResponseMessage> AllocateAsync(RoleAssignment caller, OrgUnit unit, params Course[] courses) =>
        _host.SendAsync(HttpMethod.Post, Routes.CourseAllocations, caller, new AllocateCoursesRequest(unit.Id, courses.Select(c => c.Id).ToList()));

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    private async Task<OrgUnit> AddChildAsync(OrgUnit parent)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var child = OrgUnit.CreateChild(parent, parent.LevelTypeId, "ابن", "Child", "K" + Guid.NewGuid().ToString("N")[..8]);
        db.Add(child);
        await db.SaveChangesAsync();
        return child;
    }

    private async Task<UnitCoursesDto> UnitCoursesAsync(RoleAssignment caller, OrgUnit unit)
    {
        var response = await _host.SendAsync(HttpMethod.Get, Routes.UnitCourses(unit.Id), caller);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UnitCoursesDto>(Web))!;
    }

    [Fact]
    public async Task Root_course_allocated_at_school_is_available_to_its_grades()
    {
        var (tree, admin) = await SetUpAsync("AL1");
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var grade = await AddChildAsync(tree.School1);

        var response = await AllocateAsync(admin, tree.School1, course);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<List<CourseAllocationDto>>(Web))!;
        Assert.Equal([course.Id], created.Select(a => a.CourseId));
        Assert.False(created[0].IsInherited);
        Assert.Equal(tree.School1.Id, created[0].OrgUnitId);

        var atSchool = await UnitCoursesAsync(admin, tree.School1);
        var atGrade = await UnitCoursesAsync(admin, grade);
        var atSibling = await UnitCoursesAsync(admin, tree.School2);
        Assert.Equal([course.Id], atSchool.Available.Select(a => a.CourseId));
        Assert.Equal([course.Id], atGrade.Available.Select(a => a.CourseId));
        Assert.True(atGrade.Available[0].IsInherited);
        Assert.Empty(atSibling.Available);
        Assert.Equal([course.Id], (await UnitCoursesAsync(admin, tree.Root)).AllocatedBelow.Select(a => a.CourseId));
    }

    [Fact]
    public async Task Course_owned_by_School1_cannot_be_allocated_in_School2()
    {
        var (tree, admin) = await SetUpAsync("AL2");
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.School1);
        var school2Admin = await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var byRoot = await AllocateAsync(admin, tree.School2, course);
        var bySchool2 = await AllocateAsync(school2Admin, tree.School2, course);

        Assert.Equal(HttpStatusCode.BadRequest, byRoot.StatusCode);
        Assert.Equal(ProblemCodes.course_not_available, await CodeAsync(byRoot));
        Assert.Equal(HttpStatusCode.NotFound, bySchool2.StatusCode); // the course is invisible to School 2
    }

    [Fact]
    public async Task Duplicate_allocation_is_409_and_the_request_is_all_or_nothing()
    {
        var (tree, admin) = await SetUpAsync("AL3");
        var first = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var second = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        Assert.Equal(HttpStatusCode.Created, (await AllocateAsync(admin, tree.School1, first)).StatusCode);

        var duplicate = await AllocateAsync(admin, tree.School1, second, first);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ProblemCodes.allocation_exists, await CodeAsync(duplicate));
        Assert.Equal([first.Id], (await UnitCoursesAsync(admin, tree.School1)).Available.Select(a => a.CourseId));
    }

    [Fact]
    public async Task Inactive_course_cannot_be_allocated()
    {
        var (tree, admin) = await SetUpAsync("AL4");
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        await _host.SendAsync(HttpMethod.Put, Routes.CourseStatus(course.Id), admin, new SetCourseStatusRequest(CourseStatuses.Inactive));

        var response = await AllocateAsync(admin, tree.School1, course);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.course_not_available, await CodeAsync(response));
    }

    [Fact]
    public async Task Removing_allocation_used_by_class_is_409_unless_covered_above()
    {
        var (tree, admin) = await SetUpAsync("AL5");
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var atSchool = (await (await AllocateAsync(admin, tree.School1, course)).Content.ReadFromJsonAsync<List<CourseAllocationDto>>(Web))![0];
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        await AcademicTestKit.AddClassCourseAsync(factory, await AcademicTestKit.AddClassAsync(factory, tree.School1, year), course);

        var refused = await _host.SendAsync(HttpMethod.Delete, Routes.CourseAllocation(atSchool.Id), admin);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.allocation_in_use, await CodeAsync(refused));

        // once the root allocates it too, the school allocation is no longer the only cover
        Assert.Equal(HttpStatusCode.Created, (await AllocateAsync(admin, tree.Root, course)).StatusCode);
        var removed = await _host.SendAsync(HttpMethod.Delete, Routes.CourseAllocation(atSchool.Id), admin);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.CourseAllocation(atSchool.Id), admin)).StatusCode);
    }

    [Fact]
    public async Task Allocate_at_creation_creates_rows_in_same_transaction()
    {
        var (tree, admin) = await SetUpAsync("AL6");
        var code = AcademicTestKit.NewCode();
        var good = new SaveCourseRequest(code, "مادة", "Course", null, null, "en", 3, tree.Root.Id, [tree.School1.Id, tree.School2.Id]);
        var bad = new SaveCourseRequest(AcademicTestKit.NewCode(), "مادة", "Course", null, null, "en", 3, tree.School1.Id, [tree.School1.Id, tree.School2.Id]);

        var created = await _host.SendAsync(HttpMethod.Post, Routes.Courses, admin, good);
        var failed = await _host.SendAsync(HttpMethod.Post, Routes.Courses, admin, bad);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = (await created.Content.ReadFromJsonAsync<CourseDetailDto>(Web))!;
        Assert.Equal(2, (await UnitCoursesAsync(admin, tree.Root)).AllocatedBelow.Count(a => a.CourseId == detail.Summary.Id));
        // School2 is outside the owner subtree: the whole request fails and the course is not created
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        Assert.Equal(ProblemCodes.course_not_available, await CodeAsync(failed));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<Course>().AnyAsync(c => c.Code == bad.Code.ToUpperInvariant()));
    }

    [Fact]
    public async Task Supervisor_allocates_only_supervised_courses()
    {
        var (tree, admin) = await SetUpAsync("AL7");
        var supervisor = await AccessTestData.AssignAsync(
            factory, tree, tree.Root, BuiltInRoles.Supervisor, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var mine = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var other = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseSupervisor(await db.Set<Course>().SingleAsync(c => c.Id == mine.Id), supervisor.UserId));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await AllocateAsync(supervisor, tree.School1, other)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AllocateAsync(supervisor, tree.School1, mine)).StatusCode);
        Assert.NotNull(admin);
    }

    [Fact]
    public async Task Teacher_cannot_allocate_and_other_client_unit_is_404()
    {
        var (tree, admin) = await SetUpAsync("AL8");
        var other = await AccessTestData.SeedAsync(factory, "AL8B");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.Root);

        Assert.Equal(HttpStatusCode.Forbidden, (await AllocateAsync(teacher, tree.School1, course)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AllocateAsync(admin, other.Root, course)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _host.SendAsync(HttpMethod.Post, Routes.CourseAllocations, admin,
            new AllocateCoursesRequest(tree.School1.Id, []))).StatusCode);
    }

    [Fact]
    public async Task Allocation_is_audited()
    {
        var (tree, admin) = await SetUpAsync("AL9");
        var course = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var allocation = (await (await AllocateAsync(admin, tree.School1, course)).Content.ReadFromJsonAsync<List<CourseAllocationDto>>(Web))![0];
        await _host.SendAsync(HttpMethod.Delete, Routes.CourseAllocation(allocation.Id), admin);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var actions = await db.AuditEntries.Where(e => e.EntityId == allocation.Id && e.EntityType == nameof(CourseAllocation))
            .OrderBy(e => e.TimestampUtc).Select(e => e.Action).ToListAsync();

        Assert.Equal([AuditAction.Created, AuditAction.Deleted], actions);
    }
}
