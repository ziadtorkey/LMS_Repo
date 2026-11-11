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
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
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

    private static async Task<string?> ClassTestHostCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("code").GetString();
}
