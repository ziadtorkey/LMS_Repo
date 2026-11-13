using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Courses;

/// <summary>Course restrictions API (M3 Task 8, FR-CRS-03): replace-all, validated, enrollments untouched.</summary>
[Collection(ApiCollection.Name)]
public sealed class RestrictionTests(LmsApiFactory factory) : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin, Course Course)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin, await AcademicTestKit.AddCourseAsync(factory, tree.Root));
    }

    private Task<HttpResponseMessage> PutAsync(RoleAssignment caller, Guid courseId, params CourseRestrictionInput[] inputs) =>
        _host.SendAsync(HttpMethod.Put, Routes.CourseRestrictions(courseId), caller, new SetCourseRestrictionsRequest(inputs));

    private static CourseRestrictionInput R(string type, string[] values) => new(type, values);

    private static async Task<List<CourseRestrictionDto>> ListAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<List<CourseRestrictionDto>>(Web))!;

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString();

    [Fact]
    public async Task Islamic_religion_course_restricted_to_Muslim()
    {
        var (_, admin, course) = await SetUpAsync("RS1");

        var response = await PutAsync(admin, course.Id, R(CourseRestrictionTypes.Religion, [Religions.Muslim]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([new CourseRestrictionDto("Religion", ["Muslim"])], await ListAsync(response), new RestrictionComparer());
        var detail = await _host.SendAsync(HttpMethod.Get, Routes.Course(course.Id), admin);
        var dto = (await detail.Content.ReadFromJsonAsync<CourseDetailDto>(Web))!;
        Assert.Equal("Religion", Assert.Single(dto.Restrictions).Type);
    }

    [Fact]
    public async Task Unknown_value_or_duplicate_type_is_400()
    {
        var (_, admin, course) = await SetUpAsync("RS2");

        foreach (var bad in new CourseRestrictionInput[][]
        {
            [R("Religion", ["Pastafarian"])],
            [R("Gender", ["Robot"])],
            [R("Colour", ["Red"])],
            [R("Religion", ["Muslim"]), R("Religion", ["Christian"])],
            [R("Religion", [])],
        })
        {
            var response = await PutAsync(admin, course.Id, bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.validation_failed, await CodeAsync(response));
        }

        Assert.Empty(await RowsAsync(course.Id));
    }

    [Fact]
    public async Task Level_type_of_other_client_is_400()
    {
        var (tree, admin, course) = await SetUpAsync("RS3");
        var other = await AccessTestData.SeedAsync(factory, "RS3B");
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var foreign = await db.Set<LevelType>().FirstAsync(l => l.OrganizationId == other.Org.Id);
        var own = await db.Set<LevelType>().FirstAsync(l => l.OrganizationId == tree.Org.Id);

        var rejected = await PutAsync(admin, course.Id, R("LevelType", [foreign.Id.ToString()]));
        var garbage = await PutAsync(admin, course.Id, R("LevelType", ["not-a-guid"]));
        var accepted = await PutAsync(admin, course.Id, R("LevelType", [own.Id.ToString()]));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(ProblemCodes.invalid_level_type, await CodeAsync(rejected));
        Assert.Equal(HttpStatusCode.BadRequest, garbage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Clearing_restrictions_makes_course_unrestricted()
    {
        var (_, admin, course) = await SetUpAsync("RS4");
        await PutAsync(admin, course.Id, R("Religion", ["Muslim"]), R("Gender", ["Female"]));
        Assert.Equal(2, (await RowsAsync(course.Id)).Count);

        var response = await PutAsync(admin, course.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ListAsync(response));
        Assert.Empty(await RowsAsync(course.Id));
    }

    [Fact]
    public async Task Replace_all_keeps_one_row_per_type()
    {
        var (_, admin, course) = await SetUpAsync("RS5");
        await PutAsync(admin, course.Id, R("Religion", ["Muslim"]), R("Gender", ["Female"]));

        await PutAsync(admin, course.Id, R("Religion", ["Muslim", "Christian"]));

        var rows = await RowsAsync(course.Id);
        var row = Assert.Single(rows);
        Assert.Equal(["Muslim", "Christian"], row.Values);
    }

    [Fact]
    public async Task Affected_enrollments_are_counted_not_dropped()
    {
        var (tree, admin, course) = await SetUpAsync("RS6");
        var year = await AcademicTestKit.AddYearAsync(factory, tree.School1);
        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        await AddEnrolledAsync(tree, cls, classCourse, "muslim");
        await AddEnrolledAsync(tree, cls, classCourse, "christian");

        var response = await PutAsync(admin, course.Id, R("Religion", ["Muslim"]));

        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Lms-Affected-Enrollments")));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(
            2, await db.Set<CourseEnrollment>().CountAsync(e => e.ClassCourseId == classCourse.Id && e.Status == CourseEnrollment.ActiveStatus));

        var cleared = await PutAsync(admin, course.Id);
        Assert.Equal("0", Assert.Single(cleared.Headers.GetValues("X-Lms-Affected-Enrollments")));
    }

    [Fact]
    public async Task Restriction_change_is_audited()
    {
        var (_, admin, course) = await SetUpAsync("RS7");

        await PutAsync(admin, course.Id, R("Gender", ["Female"]));
        await PutAsync(admin, course.Id, R("Gender", ["Male"]));
        await PutAsync(admin, course.Id);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var rowId = (await db.Set<CourseRestriction>().IgnoreQueryFilters().SingleAsync(r => r.CourseId == course.Id)).Id; // test read-back of a soft-deleted row
        var actions = await db.AuditEntries.Where(e => e.EntityId == rowId && e.EntityType == nameof(CourseRestriction))
            .OrderBy(e => e.TimestampUtc).Select(e => e.Action).ToListAsync();
        Assert.Equal(3, actions.Count);
        Assert.Equal(AuditAction.Created, actions[0]);
    }

    [Fact]
    public async Task Teacher_cannot_set_restrictions_and_unknown_course_is_404()
    {
        var (tree, admin, course) = await SetUpAsync("RS8");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(teacher, course.Id, R("Gender", ["Male"]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(admin, Guid.NewGuid(), R("Gender", ["Male"]))).StatusCode);
    }

    private async Task<Guid> AddEnrolledAsync(AccessTree tree, Class cls, ClassCourse classCourse, string religion)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var tracked = await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(s => s.Id == student.Id);
        tracked.Update(
            tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, "female", religion, "EG", null, null, null, null);
        var placement = await AcademicTestKit.PlaceAsync(factory, cls, student);
        db.Add(new CourseEnrollment(classCourse, placement, "Auto", new DateOnly(2041, 9, 1)));
        await db.SaveChangesAsync();
        return student.Id;
    }

    private async Task<List<CourseRestriction>> RowsAsync(Guid courseId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<CourseRestriction>().Where(r => r.CourseId == courseId).ToListAsync();
    }

    private sealed class RestrictionComparer : IEqualityComparer<CourseRestrictionDto>
    {
        public bool Equals(CourseRestrictionDto? x, CourseRestrictionDto? y) =>
            x is not null && y is not null && x.Type == y.Type && x.Values.SequenceEqual(y.Values);

        public int GetHashCode(CourseRestrictionDto obj) => obj.Type.GetHashCode(StringComparison.Ordinal);
    }
}
