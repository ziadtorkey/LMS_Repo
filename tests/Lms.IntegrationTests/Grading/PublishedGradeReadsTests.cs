using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 9: the student, guardian and staff-preview reads of published grades (L6, L18, L23).</summary>
[Collection(ApiCollection.Name)]
public sealed class PublishedGradeReadsTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private Task<GradeWorld> NewAsync(string name) => GradeReadsTestKit.NewAsync(factory, _t, name);

    private static string ChildGrades(Guid studentId) => Routes.StudentGradesPattern.Replace("{id:guid}", studentId.ToString());

    private static async Task<StudentGradesDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StudentGradesDto>())!;
    }

    [Fact]
    public async Task Student_sees_only_published_items_and_totals_from_them()
    {
        var g = await NewAsync("PG1");

        var grades = await ReadAsync(await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades));

        var course = Assert.Single(grades.Courses);
        var item = Assert.Single(course.Items);
        Assert.Equal(g.Published.Id, item.ItemId);
        Assert.Equal(8m, item.Points);
        var category = Assert.Single(course.Categories);
        Assert.Equal(g.Tests.Id, category.CategoryId);
        Assert.Equal(80m, category.Percent);
        Assert.Equal(80m, course.FinalPercent);
        Assert.Equal("P", course.BandCode);
        Assert.Equal(4m, course.BandPoints);
    }

    [Fact]
    public async Task Scheduled_item_is_hidden_until_due()
    {
        var g = await NewAsync("PG2");
        var before = await ReadAsync(await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades));
        Assert.DoesNotContain(before.Courses.Single().Items, i => i.ItemId == g.Scheduled.Id);

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<GradeItem>().SingleAsync(i => i.Id == g.Scheduled.Id)).Publish(DateTime.UtcNow); // what the scheduled job does when due
            await db.SaveChangesAsync();
        }

        var after = await ReadAsync(await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades));
        Assert.Contains(after.Courses.Single().Items, i => i.ItemId == g.Scheduled.Id);
    }

    [Fact]
    public async Task Guardian_sees_only_own_childs_published_grades()
    {
        var g = await NewAsync("PG3");

        var grades = await ReadAsync(await _t.SendAsync(g.Guardian, HttpMethod.Get, ChildGrades(g.W.StudentProfileId)));

        Assert.Equal(g.W.StudentProfileId, grades.StudentId);
        Assert.Equal(g.Published.Id, Assert.Single(grades.Courses.Single().Items).ItemId);
    }

    [Fact]
    public async Task Guardian_of_two_children_cannot_read_one_under_the_other_id()
    {
        var g = await NewAsync("PG4");

        var response = await _t.SendAsync(g.StrangerGuardian, HttpMethod.Get, ChildGrades(g.W.StudentProfileId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Inactive_guardian_link_is_404()
    {
        var g = await NewAsync("PG5");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var link = await db.Set<Lms.Domain.People.StudentGuardian>().Where(l => l.StudentId == g.W.StudentProfileId).ToListAsync();
            Assert.Single(link);
            db.RemoveRange(link);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(g.Guardian, HttpMethod.Get, ChildGrades(g.W.StudentProfileId))).StatusCode);
    }

    [Fact]
    public async Task Guardian_and_staff_are_403_on_the_students_own_route()
    {
        var g = await NewAsync("PG6");

        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(g.Guardian, HttpMethod.Get, Routes.MeGrades)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(g.W.Teacher, HttpMethod.Get, Routes.MeGrades)).StatusCode);
    }

    [Fact]
    public async Task Gpa_shows_band_points_per_course_only()
    {
        var g = await NewAsync("PG7");

        var grades = await ReadAsync(await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades));

        Assert.Equal(4m, grades.Courses.Single().BandPoints);
        Assert.DoesNotContain("overall", await (await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades)).Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Band_legend_is_returned_in_both_languages()
    {
        var g = await NewAsync("PG8");

        var legend = (await ReadAsync(await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades))).Courses.Single().Legend;

        Assert.Equal(2, legend.Bands.Count);
        Assert.All(legend.Bands, b => Assert.False(string.IsNullOrWhiteSpace(b.LabelAr) || string.IsNullOrWhiteSpace(b.LabelEn)));
        Assert.Equal("Letter", legend.Kind);
        Assert.False(string.IsNullOrWhiteSpace(legend.ScaleNameAr));
    }

    [Fact]
    public async Task Staff_preview_equals_the_student_view()
    {
        var g = await NewAsync("PG9");

        var own = (await ReadAsync(await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades))).Courses.Single();
        var response = await _t.SendAsync(g.W.Teacher, HttpMethod.Get, Routes.ClassCourseGradebookStudentView(g.W.ClassCourse.Id, g.W.StudentProfileId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<StudentCourseGradesDto>())!;

        Assert.Equal(own.Items.Select(i => i.ItemId), preview.Items.Select(i => i.ItemId));
        Assert.Equal(own.FinalPercent, preview.FinalPercent);
        Assert.Equal(own.BandCode, preview.BandCode);
        Assert.Equal(own.Categories.Select(c => c.Percent), preview.Categories.Select(c => c.Percent));
    }

    [Fact]
    public async Task Student_cannot_call_the_staff_preview()
    {
        var g = await NewAsync("PG10");

        var response = await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.ClassCourseGradebookStudentView(g.W.ClassCourse.Id, g.W.StudentProfileId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Newer_score_marker_never_reaches_students()
    {
        var g = await NewAsync("PG11");

        var json = await (await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("newer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rank", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("average", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Read_never_writes_rows()
    {
        var g = await NewAsync("PG12");
        async Task<int> CountAsync()
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            return await db.Set<Gradebook>().CountAsync() + await db.Set<GradeItem>().CountAsync() + await db.Set<GradeEntry>().CountAsync()
                + await db.Set<GradeCategory>().CountAsync();
        }

        var before = await CountAsync();
        await _t.SendAsync(g.W.Student, HttpMethod.Get, Routes.MeGrades);
        await _t.SendAsync(g.Guardian, HttpMethod.Get, ChildGrades(g.W.StudentProfileId));

        Assert.Equal(before, await CountAsync());
    }
}
