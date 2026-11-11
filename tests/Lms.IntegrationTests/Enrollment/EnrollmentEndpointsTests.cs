using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Enrollment;

[Collection(ApiCollection.Name)]
public sealed class EnrollmentEndpointsTests(LmsApiFactory factory) : IDisposable
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(EnrollmentWorld World, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var w = await EnrollmentWorld.CreateAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (w, admin);
    }

    private Task<HttpResponseMessage> SendAsync(RoleAssignment caller, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, caller, body);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage r) => (await JsonAsync(r)).GetProperty("code").GetString();

    private async Task<Guid> NewStudentIdAsync(EnrollmentWorld w, string? gender = null)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        if (gender is not null)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var tracked = await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.Update(tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, gender, tracked.Religion, tracked.Nationality, null, null, null, null);
            await db.SaveChangesAsync();
        }

        return student.Id;
    }

    private async Task<AddStudentsResultDto> AddOkAsync(RoleAssignment caller, Class cls, params Guid[] studentIds)
    {
        var response = await SendAsync(caller, HttpMethod.Post, Routes.ClassStudents(cls.Id), new AddStudentsToClassRequest(studentIds, null, false));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AddStudentsResultDto>())!;
    }

    [Fact]
    public async Task Admin_places_students_lists_them_and_removes_one()
    {
        var (w, admin) = await SetUpAsync("EE1");
        var first = await NewStudentIdAsync(w);
        var second = await NewStudentIdAsync(w);

        var result = await AddOkAsync(admin, w.Class, first, second);

        Assert.Equal((2, 4, false), (result.Added, result.EnrollmentsCreated, result.OverCapacity)); // 2 students x 2 mandatory courses
        Assert.Empty(result.RestrictionSkips);
        var list = (await (await SendAsync(admin, HttpMethod.Get, Routes.ClassStudents(w.Class.Id))).Content.ReadFromJsonAsync<List<ClassStudentDto>>())!;
        Assert.Equal(2, list.Count);
        Assert.All(list, s => Assert.Equal(ClassStudent.ActiveStatus, s.Status));

        var gone = list.First(s => s.StudentId == first);
        var bad = await SendAsync(admin, HttpMethod.Post, Routes.ClassStudentRemove(w.Class.Id, gone.Id), new RemoveFromClassRequest(Today, " "));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var removed = await SendAsync(admin, HttpMethod.Post, Routes.ClassStudentRemove(w.Class.Id, gone.Id), new RemoveFromClassRequest(Today, "Left"));
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var enrollments = (await (await SendAsync(admin, HttpMethod.Get, Routes.StudentEnrollments(first) + "?status=Active")).Content
            .ReadFromJsonAsync<PagedResult<EnrollmentDto>>())!;
        Assert.Empty(enrollments.Items);
    }

    [Fact]
    public async Task Adding_students_validates_the_request_and_hides_students_outside_the_scope()
    {
        var (w, admin) = await SetUpAsync("EE2");
        var inside = await NewStudentIdAsync(w);
        var other = await EnrollmentWorld.CreateAsync(factory, "EE2B");
        var outsider = await NewStudentIdAsync(other);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Post, Routes.ClassStudents(w.Class.Id),
            new AddStudentsToClassRequest([], null, false))).StatusCode);
        var tooMany = await SendAsync(admin, HttpMethod.Post, Routes.ClassStudents(w.Class.Id),
            new AddStudentsToClassRequest(Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList(), null, false));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, await CodeAsync(tooMany));

        // One student of another client in the list: the whole request is 404 and nothing is placed.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(admin, HttpMethod.Post, Routes.ClassStudents(w.Class.Id),
            new AddStudentsToClassRequest([inside, outsider], null, false))).StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.Set<ClassStudent>().AnyAsync(c => c.StudentId == inside));
    }

    [Fact]
    public async Task Placement_reports_restriction_skips_and_conflicts_with_codes()
    {
        var (w, admin) = await SetUpAsync("EE3");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(w.A, CourseRestrictionEvaluator.Gender, ["male"]));
            await db.SaveChangesAsync();
        }

        var girl = await NewStudentIdAsync(w, "female");

        var result = await AddOkAsync(admin, w.Class, girl);
        var skip = Assert.Single(result.RestrictionSkips);
        Assert.Equal((girl, w.CcA.Id), (skip.StudentId, skip.ClassCourseId));
        Assert.Equal(["Gender"], skip.FailedTypes.ToArray());

        var again = await SendAsync(admin, HttpMethod.Post, Routes.ClassStudents(w.Class.Id), new AddStudentsToClassRequest([girl], null, false));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(ProblemCodes.already_placed, await CodeAsync(again));
    }

    [Fact]
    public async Task Manual_enrollment_in_an_optional_course_is_listed_and_can_be_dropped()
    {
        var (w, admin) = await SetUpAsync("EE4");
        var student = await NewStudentIdAsync(w);
        await AddOkAsync(admin, w.Class, student);

        var enrolled = await SendAsync(admin, HttpMethod.Post, Routes.ClassCourseEnrollments(w.CcOptional.Id), new EnrollStudentsRequest([student]));
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var dto = Assert.Single((await enrolled.Content.ReadFromJsonAsync<List<EnrollmentDto>>())!);
        Assert.Equal((EnrollmentSources.Manual, EnrollmentStatuses.Active, w.CcOptional.Id), (dto.Source, dto.Status, dto.ClassCourseId));
        Assert.False(string.IsNullOrEmpty(dto.CourseNameEn)); // the course is owned at the root, above the class unit: names still resolve

        var mandatory = await SendAsync(admin, HttpMethod.Post, Routes.ClassCourseEnrollments(w.CcA.Id), new EnrollStudentsRequest([student]));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.enrollment_not_optional), (mandatory.StatusCode, await CodeAsync(mandatory)));
        var twice = await SendAsync(admin, HttpMethod.Post, Routes.ClassCourseEnrollments(w.CcOptional.Id), new EnrollStudentsRequest([student]));
        Assert.Equal(ProblemCodes.already_enrolled, await CodeAsync(twice));

        var drop = await SendAsync(admin, HttpMethod.Post, Routes.EnrollmentDrop(dto.Id), new DropEnrollmentRequest("Changed mind", null));
        Assert.Equal(HttpStatusCode.NoContent, drop.StatusCode);
        var listed = (await (await SendAsync(admin, HttpMethod.Get, Routes.ClassCourseEnrollments(w.CcOptional.Id) + "?status=Dropped")).Content
            .ReadFromJsonAsync<PagedResult<EnrollmentDto>>())!;
        var dropped = Assert.Single(listed.Items);
        Assert.Equal(("Changed mind", EnrollmentStatuses.Dropped), (dropped.DropReason, dropped.Status));
    }

    [Fact]
    public async Task Manual_enrollment_of_a_restricted_student_is_409_with_failed_types_and_all_or_nothing()
    {
        var (w, admin) = await SetUpAsync("EE5");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(w.Optional, CourseRestrictionEvaluator.Gender, ["male"]));
            await db.SaveChangesAsync();
        }

        var boy = await NewStudentIdAsync(w, "male");
        var girl = await NewStudentIdAsync(w, "female");
        await AddOkAsync(admin, w.Class, boy, girl);

        var response = await SendAsync(admin, HttpMethod.Post, Routes.ClassCourseEnrollments(w.CcOptional.Id), new EnrollStudentsRequest([boy, girl]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(ProblemCodes.restriction_not_met, body.GetProperty("code").GetString());
        Assert.Equal(["Gender"], body.GetProperty("failedTypes").EnumerateArray().Select(e => e.GetString()!).ToArray());
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await read.Set<CourseEnrollment>().AnyAsync(e => e.ClassCourseId == w.CcOptional.Id)); // the boy was rolled back too
    }

    [Fact]
    public async Task Teacher_reads_enrollments_of_own_class_course_only()
    {
        var (w, admin) = await SetUpAsync("EE6");
        var otherClass = await AcademicTestKit.AddClassAsync(factory, w.Tree.School1, w.Year);
        var otherCourse = await AcademicTestKit.AddClassCourseAsync(factory, otherClass, await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root));
        var student = await NewStudentIdAsync(w);
        var elsewhere = await NewStudentIdAsync(w);
        await AddOkAsync(admin, w.Class, student);
        await AddOkAsync(admin, otherClass, elsewhere);

        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var ccA = await db.Set<ClassCourse>().SingleAsync(c => c.Id == w.CcA.Id);
            db.Add(new ClassCourseTeacher(ccA, teacher.UserId, ClassCourseTeacher.PrimaryRole, Today));
            await db.SaveChangesAsync();
        }

        // Own class course: visible. Another class course of the same unit, even in the same class: not found.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassCourseEnrollments(w.CcA.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassCourseEnrollments(w.CcB.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassCourseEnrollments(otherCourse.Id))).StatusCode);

        // Lists never contain the others'.
        var all = (await (await SendAsync(teacher, HttpMethod.Get, Routes.Enrollments)).Content.ReadFromJsonAsync<PagedResult<EnrollmentDto>>())!;
        Assert.Equal([w.CcA.Id], all.Items.Select(e => e.ClassCourseId).Distinct().ToArray());
        var ofStudent = (await (await SendAsync(teacher, HttpMethod.Get, Routes.StudentEnrollments(student))).Content.ReadFromJsonAsync<PagedResult<EnrollmentDto>>())!;
        Assert.Equal([w.CcA.Id], ofStudent.Items.Select(e => e.ClassCourseId).ToArray()); // not the student's course B
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(teacher, HttpMethod.Get, Routes.StudentEnrollments(elsewhere))).StatusCode);

        // Class students: only for a class where they teach.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassStudents(w.Class.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassStudents(otherClass.Id))).StatusCode);

        // And never a write.
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(teacher, HttpMethod.Post, Routes.ClassStudents(w.Class.Id),
            new AddStudentsToClassRequest([elsewhere], null, false))).StatusCode);
    }

    [Fact]
    public async Task Homeroom_teacher_sees_the_class_students()
    {
        var (w, admin) = await SetUpAsync("EE7");
        var student = await NewStudentIdAsync(w);
        await AddOkAsync(admin, w.Class, student);
        var teacher = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassStudents(w.Class.Id))).StatusCode);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == w.Class.Id);
            cls.Update(cls.Code, cls.NameAr, cls.NameEn, cls.Capacity, teacher.UserId);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(teacher, HttpMethod.Get, Routes.ClassStudents(w.Class.Id))).StatusCode);
    }

    [Fact]
    public async Task Closed_year_blocks_writes_over_http_and_other_subtrees_get_404()
    {
        var (w, admin) = await SetUpAsync("EE8");
        var student = await NewStudentIdAsync(w);
        var late = await NewStudentIdAsync(w);
        await AddOkAsync(admin, w.Class, student);
        var school2Admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(school2Admin, HttpMethod.Get, Routes.ClassStudents(w.Class.Id))).StatusCode);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AcademicYear>().SingleAsync(y => y.Id == w.Year.Id)).Close();
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(admin, HttpMethod.Post, Routes.ClassStudents(w.Class.Id), new AddStudentsToClassRequest([late], null, false));

        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.year_closed), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task Paging_and_status_filters_are_validated()
    {
        var (w, admin) = await SetUpAsync("EE9");

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, Routes.Enrollments + "?pageSize=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, Routes.Enrollments + "?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(admin, HttpMethod.Get, Routes.Enrollments + "?status=Weird")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(admin, HttpMethod.Get, Routes.Enrollments + "?classCourseId=" + w.CcA.Id)).StatusCode);
    }
}
