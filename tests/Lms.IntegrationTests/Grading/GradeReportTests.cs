using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Courses;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 9: grade reports per student, class and course; who may read them and what they count.</summary>
[Collection(ApiCollection.Name)]
public sealed class GradeReportTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static string Url(string kind, string query) => Routes.GradeReport(kind) + "?" + query;

    private static async Task<GradeReportDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<GradeReportDto>())!;
    }

    [Fact]
    public async Task Course_report_counts_published_and_unpublished_items_for_staff()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GRP1");

        var report = await ReadAsync(await _t.SendAsync(g.W.Admin, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id)));

        var row = Assert.Single(report.Rows.Items);
        Assert.Equal(g.W.StudentProfileId, row.StudentId);
        // (8 + 2) / (10 + 20) over Tests = 33.33; the scheduled item sits in a weight 0 category. Staff see the unpublished score, students do not.
        Assert.Equal(33.33m, row.FinalPercent);
        Assert.Equal("F", row.BandCode);
        Assert.True(report.ComputedAtUtc > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task Class_and_student_reports_return_the_same_row()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GRP2");

        var byClass = await ReadAsync(await _t.SendAsync(g.W.Admin, HttpMethod.Get, Url("class", "classId=" + g.W.Class.Id)));
        var byStudent = await ReadAsync(await _t.SendAsync(g.W.Admin, HttpMethod.Get, Url("student", "studentId=" + g.W.StudentProfileId)));

        Assert.Equal(byClass.Rows.Items.Single().FinalPercent, byStudent.Rows.Items.Single().FinalPercent);
        Assert.Equal(1, byClass.Rows.Total);
    }

    [Fact]
    public async Task Missing_scope_parameter_is_400_and_unknown_kind_is_404()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GRP3");

        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(g.W.Admin, HttpMethod.Get, Url("course", "termId=" + Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(g.W.Admin, HttpMethod.Get, Url("galaxy", "classId=" + g.W.Class.Id))).StatusCode);
    }

    [Fact]
    public async Task Counselor_cannot_read_grades()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GRP4");
        var counselor = await AccessTestData.AssignAsync(
            factory, g.W.Tree, g.W.Tree.School1, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id);

        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(counselor, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(g.W.Teacher, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(g.W.Student, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(g.Guardian, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id))).StatusCode);
    }

    [Fact]
    public async Task Supervisor_reports_only_own_courses()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GRP5");
        var user = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var supervisor = await AccessTestData.AssignAsync(factory, g.W.Tree, g.W.Tree.School1, BuiltInRoles.Supervisor, user);

        // Not supervising anything yet: the course is 404 (it does not exist for them).
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(supervisor, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id))).StatusCode);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseSupervisor(await db.Set<Course>().SingleAsync(c => c.Id == g.W.Course.Id), user));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(supervisor, HttpMethod.Get, Url("course", "classCourseId=" + g.W.ClassCourse.Id))).StatusCode);
    }
}
