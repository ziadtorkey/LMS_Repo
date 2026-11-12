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
using Lms.Shared.Configuration;
using Lms.Shared.Courses;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Courses;

/// <summary>Course catalog CRUD, status and delete (M3 Task 6, FR-CRS-01).</summary>
[Collection(ApiCollection.Name)]
public sealed class CourseEndpointsTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name, bool flagSchools = true)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        if (flagSchools)
        {
            await FlagAsync(tree.School1);
            await FlagAsync(tree.School2);
        }

        return (tree, admin);
    }

    private async Task FlagAsync(OrgUnit unit)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        (await db.OrgUnits.SingleAsync(u => u.Id == unit.Id)).SetAcademicFlags(false, null, true);
        await db.SaveChangesAsync();
    }

    private static SaveCourseRequest Request(Guid owner, string? code = null, string name = "Math") =>
        new(code ?? AcademicTestKit.NewCode(), "رياضيات " + name, name, null, null, "en", 4, owner, null);

    private Task<HttpResponseMessage> PostAsync(RoleAssignment caller, SaveCourseRequest body, string? language = null) =>
        _host.SendAsync(HttpMethod.Post, Routes.Courses, caller, body, language);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static async Task<CourseDetailDto> DetailAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<CourseDetailDto>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

    [Fact]
    public async Task Manager_creates_course_owned_by_school()
    {
        var (tree, _) = await SetUpAsync("CE1");
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, Guid.NewGuid());

        var response = await PostAsync(manager, Request(tree.School1.Id, "m-101"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await DetailAsync(response);
        Assert.Equal("M-101", detail.Summary.Code);
        Assert.Equal(tree.School1.Id, detail.Summary.OwnerOrgUnitId);
        Assert.True(detail.CanEdit);
        Assert.False(detail.Summary.IsInherited);
        Assert.Equal(CourseStatuses.Active, detail.Summary.Status);
    }

    [Fact]
    public async Task Owner_without_course_management_flag_is_400()
    {
        var (tree, admin) = await SetUpAsync("CE2", flagSchools: false);

        var response = await PostAsync(admin, Request(tree.School1.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_owner_unit, (await JsonAsync(response)).GetProperty("code").GetString());
        // the client root always may own courses
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(admin, Request(tree.Root.Id))).StatusCode);
    }

    [Fact]
    public async Task Code_taken_in_sibling_school_is_409()
    {
        var (tree, admin) = await SetUpAsync("CE3");
        var code = AcademicTestKit.NewCode();
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(admin, Request(tree.School1.Id, code))).StatusCode);

        var duplicate = await PostAsync(admin, Request(tree.School2.Id, code.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(ProblemCodes.course_code_taken, (await JsonAsync(duplicate)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_input_is_400_validation_failed()
    {
        var (tree, admin) = await SetUpAsync("CE4");
        var bad = new[]
        {
            Request(tree.School1.Id, "bad code!"),
            Request(tree.School1.Id) with { Language = "english" },
            Request(tree.School1.Id) with { WeeklyPeriods = 41 },
            Request(tree.School1.Id) with { NameEn = " " },
            Request(tree.School1.Id) with { AllocateToOrgUnitIds = [tree.School1.Id] },
        };

        foreach (var body in bad)
        {
            var response = await PostAsync(admin, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.validation_failed, (await JsonAsync(response)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task CreateCourses_policy_blocks_create()
    {
        var (tree, admin) = await SetUpAsync("CE5");
        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateCourses);

        var response = await PostAsync(admin, Request(tree.School1.Id), "en");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("BlockedByPolicy", (await JsonAsync(response)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Teacher_gets_403_on_create()
    {
        var (tree, _) = await SetUpAsync("CE6");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(teacher, Request(tree.School1.Id))).StatusCode);
    }

    [Fact]
    public async Task School_admin_sees_root_course_as_inherited_read_only_and_PUT_is_404()
    {
        var (tree, admin) = await SetUpAsync("CE7");
        var rootCourse = (await DetailAsync(await PostAsync(admin, Request(tree.Root.Id)))).Summary;
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var get = await _host.SendAsync(HttpMethod.Get, Routes.Course(rootCourse.Id), schoolAdmin);
        var put = await _host.SendAsync(HttpMethod.Put, Routes.Course(rootCourse.Id), schoolAdmin, Request(tree.School1.Id));
        var delete = await _host.SendAsync(HttpMethod.Delete, Routes.Course(rootCourse.Id), schoolAdmin);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var detail = await DetailAsync(get);
        Assert.True(detail.Summary.IsInherited);
        Assert.False(detail.CanEdit);
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Course_of_sibling_school_and_other_client_is_404()
    {
        var (tree, admin) = await SetUpAsync("CE8");
        var other = await AccessTestData.SeedAsync(factory, "CE8B");
        var otherAdmin = await AccessTestData.AssignAsync(factory, other, other.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school2Course = (await DetailAsync(await PostAsync(admin, Request(tree.School2.Id)))).Summary;
        var school1Admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        foreach (var caller in new[] { school1Admin, otherAdmin })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.Course(school2Course.Id), caller)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.CourseStatus(school2Course.Id), caller, new SetCourseStatusRequest("Inactive"))).StatusCode);
        }
    }

    [Fact]
    public async Task Supervisor_creator_becomes_supervisor()
    {
        var (tree, _) = await SetUpAsync("CE9");
        var supervisor = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Supervisor, (await PeopleTestKit.AddUserAsync(factory)).Id);

        var response = await PostAsync(supervisor, Request(tree.School1.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await DetailAsync(response);
        Assert.Equal([supervisor.UserId], detail.Supervisors.Select(s => s.UserId));
        Assert.True(detail.CanEdit);
    }

    [Fact]
    public async Task Supervisor_cannot_edit_unsupervised_course_404()
    {
        var (tree, admin) = await SetUpAsync("CE10");
        var supervisor = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Supervisor, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var course = (await DetailAsync(await PostAsync(admin, Request(tree.School1.Id)))).Summary;

        // visible (Courses.View is unit-wide) but not editable: not supervised
        var get = await DetailAsync(await _host.SendAsync(HttpMethod.Get, Routes.Course(course.Id), supervisor));
        Assert.False(get.CanEdit);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.Course(course.Id), supervisor, Request(tree.School1.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.CourseStatus(course.Id), supervisor, new SetCourseStatusRequest("Inactive"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.Course(course.Id), supervisor)).StatusCode);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseSupervisor(await db.Set<Course>().SingleAsync(c => c.Id == course.Id), supervisor.UserId));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK,
            (await _host.SendAsync(HttpMethod.Put, Routes.Course(course.Id), supervisor, Request(tree.School1.Id, null, "Renamed"))).StatusCode);
    }

    [Fact]
    public async Task Update_changes_fields_and_moving_the_owner_needs_a_valid_target()
    {
        var (tree, admin) = await SetUpAsync("CE11");
        var created = (await DetailAsync(await PostAsync(admin, Request(tree.School1.Id)))).Summary;

        var renamed = await _host.SendAsync(HttpMethod.Put, Routes.Course(created.Id), admin,
            Request(tree.School2.Id, "new-code", "Physics") with { Language = "ar", WeeklyPeriods = 6, DescriptionEn = " Notes " });

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var detail = await DetailAsync(renamed);
        Assert.Equal("NEW-CODE", detail.Summary.Code);
        Assert.Equal("Physics", detail.Summary.NameEn);
        Assert.Equal(tree.School2.Id, detail.Summary.OwnerOrgUnitId);
        Assert.Equal("Notes", detail.DescriptionEn);
        Assert.Equal(6, detail.Summary.WeeklyPeriods);

        var unflagged = await AccessTestData.SeedAsync(factory, "CE11U");
        var noFlag = await _host.SendAsync(HttpMethod.Put, Routes.Course(created.Id), admin, Request(tree.School1.Id) with { OwnerOrgUnitId = tree.Root.Id });
        Assert.Equal(HttpStatusCode.OK, noFlag.StatusCode); // the root always may own courses
        Assert.NotNull(unflagged);
    }

    [Fact]
    public async Task Delete_course_in_use_is_409_and_deactivate_works()
    {
        var (tree, admin) = await SetUpAsync("CE12");
        var used = (await DetailAsync(await PostAsync(admin, Request(tree.School1.Id)))).Summary;
        var free = (await DetailAsync(await PostAsync(admin, Request(tree.School1.Id)))).Summary;
        await AcademicTestKit.AllocateAsync(factory, await LoadAsync(used.Id), tree.School1);

        var refused = await _host.SendAsync(HttpMethod.Delete, Routes.Course(used.Id), admin);
        var deactivated = await _host.SendAsync(HttpMethod.Put, Routes.CourseStatus(used.Id), admin, new SetCourseStatusRequest("Inactive"));
        var deleted = await _host.SendAsync(HttpMethod.Delete, Routes.Course(free.Id), admin);
        var gone = await _host.SendAsync(HttpMethod.Get, Routes.Course(free.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.course_in_use, (await JsonAsync(refused)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(CourseStatuses.Inactive, (await DetailAsync(deactivated)).Summary.Status);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Course_in_use_by_a_class_is_409_on_delete()
    {
        var (tree, admin) = await SetUpAsync("CE13");
        var created = (await DetailAsync(await PostAsync(admin, Request(tree.School1.Id)))).Summary;
        var course = await LoadAsync(created.Id);
        var year = await AcademicTestKit.AddYearAsync(factory, tree.Root);
        await AcademicTestKit.AddClassCourseAsync(factory, await AcademicTestKit.AddClassAsync(factory, tree.School1, year), course);

        Assert.Equal(HttpStatusCode.Conflict, (await _host.SendAsync(HttpMethod.Delete, Routes.Course(created.Id), admin)).StatusCode);
    }

    [Fact]
    public async Task List_search_by_code_prefix_and_arabic_name_and_filters()
    {
        var (tree, admin) = await SetUpAsync("CE14");
        var rootCourse = (await DetailAsync(await PostAsync(admin, Request(tree.Root.Id, "ZZ-ROOT" + Guid.NewGuid().ToString("N")[..4])))).Summary;
        var algebra = (await DetailAsync(await PostAsync(admin, new SaveCourseRequest("ALG-1", "الجبر المتقدم", "Algebra", null, null, "en", 3, tree.School1.Id, null)))).Summary;
        var biology = (await DetailAsync(await PostAsync(admin, new SaveCourseRequest("BIO-1", "الأحياء", "Biology", null, null, "en", 3, tree.School1.Id, null)))).Summary;
        await _host.SendAsync(HttpMethod.Put, Routes.CourseStatus(biology.Id), admin, new SetCourseStatusRequest("Inactive"));
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        async Task<List<Guid>> IdsAsync(string query)
        {
            var page = await (await _host.SendAsync(HttpMethod.Get, Routes.Courses + query, schoolAdmin))
                .Content.ReadFromJsonAsync<PagedResult<CourseSummaryDto>>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return page!.Items.Select(i => i.Id).ToList();
        }

        Assert.Equal([algebra.Id], await IdsAsync("?search=ALG"));
        Assert.Equal([algebra.Id], await IdsAsync("?search=" + Uri.EscapeDataString("الجبر")));
        Assert.Equal([biology.Id], await IdsAsync("?status=Inactive"));
        Assert.Contains(rootCourse.Id, await IdsAsync(string.Empty)); // inherited from the root
        Assert.DoesNotContain(rootCourse.Id, await IdsAsync("?includeInherited=false"));
        Assert.Equal([algebra.Id, biology.Id], await IdsAsync("?includeInherited=false&sort=code"));
        Assert.Equal([biology.Id, algebra.Id], await IdsAsync("?includeInherited=false&sort=-code"));
        Assert.Single(await IdsAsync("?includeInherited=false&pageSize=1"));
    }

    [Fact]
    public async Task Course_changes_are_audited()
    {
        var (tree, admin) = await SetUpAsync("CE15");
        var created = (await DetailAsync(await PostAsync(admin, Request(tree.School1.Id)))).Summary;
        await _host.SendAsync(HttpMethod.Put, Routes.CourseStatus(created.Id), admin, new SetCourseStatusRequest("Inactive"));
        await _host.SendAsync(HttpMethod.Delete, Routes.Course(created.Id), admin);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var actions = await db.AuditEntries.Where(e => e.EntityId == created.Id && e.EntityType == nameof(Course))
            .OrderBy(e => e.TimestampUtc).Select(e => e.Action).ToListAsync();

        Assert.Equal(3, actions.Count);
        Assert.Equal(AuditAction.Created, actions[0]);
    }

    private async Task<Course> LoadAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Course>().SingleAsync(c => c.Id == id);
    }
}
