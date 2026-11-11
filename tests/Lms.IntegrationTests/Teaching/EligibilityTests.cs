using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Teaching;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Teaching;

[Collection(ApiCollection.Name)]
public sealed class EligibilityTests(LmsApiFactory factory) : IDisposable
{
    private readonly TeachingTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private async Task<PagedResult<TeacherEligibilityDto>> ListAsync(Lms.Domain.Access.RoleAssignment caller, string query = "")
    {
        var response = await _t.Classes.SendAsync(caller, HttpMethod.Get, Routes.TeacherEligibility + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PagedResult<TeacherEligibilityDto>>())!;
    }

    [Fact]
    public async Task Grant_eligibility_for_math_to_two_teachers_at_school()
    {
        var w = await _t.Classes.SeedAsync("EL1", currentYear: true);
        var math = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, math, w.Tree.Root);
        var a = await _t.TeacherAsync(w);
        var b = await _t.TeacherAsync(w);

        var response = await _t.GrantAsync(w, w.Tree.School1.Id, [a.User.Id, b.User.Id], [math.Id]);
        var again = await _t.GrantAsync(w, w.Tree.School1.Id, [a.User.Id], [math.Id]); // existing pairs are skipped

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<List<TeacherEligibilityDto>>())!;
        Assert.Equal(2, created.Count);
        Assert.All(created, e => Assert.Equal(w.Tree.School1.Id, e.OrgUnitId));
        Assert.Equal(math.Code, created[0].CourseCode);
        Assert.Empty((await again.Content.ReadFromJsonAsync<List<TeacherEligibilityDto>>())!);
        Assert.Equal(2, (await ListAsync(w.Admin, $"?orgUnitId={w.Tree.School1.Id}")).Total);
        Assert.Equal(1, (await ListAsync(w.Admin, $"?orgUnitId={w.Tree.School1.Id}&userId={a.User.Id}")).Total);
        Assert.Equal(0, (await ListAsync(w.Admin, $"?orgUnitId={w.Tree.School2.Id}")).Total);
    }

    [Fact]
    public async Task Grant_refuses_users_without_teacher_role_unavailable_courses_and_oversized_batches()
    {
        var w = await _t.Classes.SeedAsync("EL2", currentYear: true);
        var math = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, math, w.Tree.Root);
        var unallocated = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School2);
        var teacher = await _t.TeacherAsync(w);
        var elsewhere = await _t.TeacherAsync(w, w.Tree.School2); // Teacher at School 2 only
        var plain = await PeopleTestKit.AddUserAsync(factory);

        var noRole = await _t.GrantAsync(w, w.Tree.School1.Id, [plain.Id], [math.Id]);
        var otherSchool = await _t.GrantAsync(w, w.Tree.School1.Id, [elsewhere.User.Id], [math.Id]);
        var badCourse = await _t.GrantAsync(w, w.Tree.School1.Id, [teacher.User.Id], [unallocated.Id]);
        var tooMany = await _t.GrantAsync(w, w.Tree.School1.Id, Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToList(),
            Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList());
        var empty = await _t.GrantAsync(w, w.Tree.School1.Id, [], [math.Id]);

        Assert.Equal(ProblemCodes.teacher_not_in_unit, await ClassTestHostCode(noRole));
        Assert.Equal(ProblemCodes.teacher_not_in_unit, await ClassTestHostCode(otherSchool));
        Assert.Equal(ProblemCodes.course_not_available, await ClassTestHostCode(badCourse));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(ProblemCodes.too_many_items, await ClassTestHostCode(tooMany));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(ProblemCodes.validation_failed, await ClassTestHostCode(empty));
        Assert.Equal(0, (await ListAsync(w.Admin, $"?orgUnitId={w.Tree.Root.Id}")).Total);
    }

    [Fact]
    public async Task Teacher_assigned_below_the_unit_counts_and_delete_keeps_current_assignments()
    {
        var s = await _t.SetUpAsync("EL3");
        var teacher = await _t.EligibleTeacherAsync(s);
        var assigned = await _t.AssignAsync(s, teacher.User.Id);
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        var eligibility = (await ListAsync(s.W.Admin, $"?orgUnitId={s.W.Tree.School1.Id}")).Items.Single();

        var delete = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Delete, Routes.TeacherEligibilityItem(eligibility.Id));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(0, (await ListAsync(s.W.Admin, $"?orgUnitId={s.W.Tree.School1.Id}")).Total);
        Assert.Single(await _t.TeachersAsync(s)); // the running assignment stays
        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Delete, Routes.TeacherEligibilityItem(eligibility.Id))).StatusCode);
    }

    [Fact]
    public async Task Eligibility_is_isolated_by_client_and_unit_and_needs_the_manage_permission()
    {
        var s = await _t.SetUpAsync("EL4");
        var teacher = await _t.EligibleTeacherAsync(s);
        var other = await _t.Classes.SeedAsync("EL4B", currentYear: true);
        var eligibility = (await ListAsync(s.W.Admin, $"?orgUnitId={s.W.Tree.School1.Id}")).Items.Single();
        var plainTeacher = await _t.TeacherAsync(s.W);

        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(other.Admin, HttpMethod.Get,
            Routes.TeacherEligibility + $"?orgUnitId={s.W.Tree.School1.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(other.Admin, HttpMethod.Delete, Routes.TeacherEligibilityItem(eligibility.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.GrantAsync(other, s.W.Tree.School1.Id, [teacher.User.Id], [s.Course.Id])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.Classes.SendAsync(plainTeacher.Assignment, HttpMethod.Post, Routes.TeacherEligibility,
            new GrantEligibilityRequest(s.W.Tree.School1.Id, [teacher.User.Id], [s.Course.Id]))).StatusCode);
        _ = factory;
    }

    [Fact]
    public async Task Supervisor_grants_and_deletes_eligibility_only_for_supervised_courses()
    {
        var w = await _t.Classes.SeedAsync("EL5", currentYear: true);
        var supervised = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        var other = await AcademicTestKit.AddCourseAsync(factory, w.Tree.School1);
        await AcademicTestKit.AllocateAsync(factory, supervised, w.Tree.School1);
        await AcademicTestKit.AllocateAsync(factory, other, w.Tree.School1);
        var teacher = await _t.TeacherAsync(w);
        var supervisorUser = await PeopleTestKit.AddUserAsync(factory);
        var supervisor = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Supervisor, supervisorUser.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new Lms.Domain.Courses.CourseSupervisor(await db.Set<Lms.Domain.Courses.Course>().SingleAsync(c => c.Id == supervised.Id), supervisorUser.Id));
            await db.SaveChangesAsync();
        }

        Task<HttpResponseMessage> Grant(params Guid[] courses) => _t.Classes.SendAsync(supervisor, HttpMethod.Post, Routes.TeacherEligibility,
            new GrantEligibilityRequest(w.Tree.School1.Id, [teacher.User.Id], courses));
        var mine = await Grant(supervised.Id);
        var notMine = await Grant(other.Id);
        var mixed = await Grant(supervised.Id, other.Id); // the answer never says which course failed
        await _t.GrantAsync(w, w.Tree.School1.Id, [teacher.User.Id], [other.Id]); // the admin grants the other one
        var rows = (await ListAsync(w.Admin, $"?orgUnitId={w.Tree.School1.Id}")).Items;
        var otherRow = rows.Single(r => r.CourseId == other.Id);
        var mineRow = rows.Single(r => r.CourseId == supervised.Id);

        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mixed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(supervisor, HttpMethod.Delete, Routes.TeacherEligibilityItem(otherRow.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _t.Classes.SendAsync(supervisor, HttpMethod.Delete, Routes.TeacherEligibilityItem(mineRow.Id))).StatusCode);
    }

    [Fact]
    public async Task Parallel_grants_of_the_same_pair_never_500()
    {
        var w = await _t.Classes.SeedAsync("EL6", currentYear: true);
        var course = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, course, w.Tree.Root);
        var teacher = await _t.TeacherAsync(w);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => _t.GrantAsync(w, w.Tree.School1.Id, [teacher.User.Id], [course.Id])));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(1, (await ListAsync(w.Admin, $"?orgUnitId={w.Tree.School1.Id}")).Total);
    }

    private static async Task<string?> ClassTestHostCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task IncludeAncestors_returns_the_eligibilities_that_apply_to_a_class_at_the_unit()
    {
        var w = await _t.Classes.SeedAsync("EL7", currentYear: true);
        var atRoot = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        var atSchool = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        var atOther = await AcademicTestKit.AddCourseAsync(factory, w.Tree.Root);
        foreach (var course in new[] { atRoot, atSchool, atOther })
        {
            await AcademicTestKit.AllocateAsync(factory, course, w.Tree.Root);
        }

        var teacher = await _t.TeacherAsync(w);
        await _t.GrantAsync(w, w.Tree.Root.Id, [teacher.User.Id], [atRoot.Id]);
        await _t.GrantAsync(w, w.Tree.School1.Id, [teacher.User.Id], [atSchool.Id]);
        await _t.GrantAsync(w, w.Tree.School2.Id, [teacher.User.Id], [atOther.Id]);
        var schoolAdminUser = await PeopleTestKit.AddUserAsync(factory);
        var schoolAdmin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.OrgAdmin, schoolAdminUser.Id);
        var query = $"?orgUnitId={w.Tree.School1.Id}&includeAncestors=true";

        var applying = await ListAsync(w.Admin, query);
        var byUnitAdmin = await ListAsync(schoolAdmin, query); // the root grant sits above the caller's subtree
        var descendants = await ListAsync(w.Admin, $"?orgUnitId={w.Tree.School1.Id}");

        Assert.Equal(2, applying.Total);
        Assert.Contains(applying.Items, e => e.CourseId == atRoot.Id);
        Assert.Contains(applying.Items, e => e.CourseId == atSchool.Id);
        Assert.DoesNotContain(applying.Items, e => e.CourseId == atOther.Id);
        Assert.Equal(2, byUnitAdmin.Total);
        Assert.Equal([atSchool.Id], descendants.Items.Select(e => e.CourseId).ToList());
    }
}
