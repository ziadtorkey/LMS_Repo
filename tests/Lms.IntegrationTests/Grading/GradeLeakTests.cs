using System.Net;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;

namespace Lms.IntegrationTests.Grading;

/// <summary>
/// M5 L6, one leak test per student-reachable endpoint: a hidden or scheduled item (and its category) never shows up in any response body of a
/// student, a guardian or a teacher of another class: not as a title, a category, a weight, a percent or a count.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GradeLeakTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static string ChildGrades(Guid studentId) => Routes.StudentGradesPattern.Replace("{id:guid}", studentId.ToString());

    private async Task<string> BodyAsync(RoleAssignment caller, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _t.SendAsync(caller, HttpMethod.Get, url);
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertNoLeak(GradeWorld g, string body)
    {
        Assert.DoesNotContain(GradeReadsTestKit.SecretMarker, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(g.Hidden.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(g.Scheduled.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(g.SecretCategory.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Student_response_never_carries_a_hidden_or_scheduled_item()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GL1");

        AssertNoLeak(g, await BodyAsync(g.W.Student, Routes.MeGrades));
    }

    [Fact]
    public async Task Guardian_response_never_carries_a_hidden_or_scheduled_item()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GL2");

        AssertNoLeak(g, await BodyAsync(g.Guardian, ChildGrades(g.W.StudentProfileId)));
    }

    [Fact]
    public async Task Hidden_item_never_changes_category_percent_final_or_counts()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GL3");

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // A score on a hidden item (and a bigger one) must not move anything a student sees.
            var entry = db.Set<Lms.Domain.Grading.GradeEntry>().Single(e => e.GradeItemId == g.Hidden.Id);
            entry.SetPoints(20m, null, DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var grades = System.Text.Json.JsonSerializer.Deserialize<StudentGradesDto>(
            await BodyAsync(g.W.Student, Routes.MeGrades), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var course = grades.Courses.Single();
        Assert.Single(course.Items);
        Assert.Single(course.Categories);
        Assert.Equal(80m, course.Categories.Single().Percent);
        Assert.Equal(80m, course.FinalPercent);
    }

    [Fact]
    public async Task Teacher_of_another_class_gets_404_from_the_preview_and_no_item_titles()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GL4");

        var body = await BodyAsync(
            g.W.OtherTeacher, Routes.ClassCourseGradebookStudentView(g.W.ClassCourse.Id, g.W.StudentProfileId), HttpStatusCode.NotFound);

        AssertNoLeak(g, body);
    }

    [Fact]
    public async Task Other_teacher_and_other_student_see_nothing_of_this_gradebook_on_their_own_routes()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GL5");

        AssertNoLeak(g, await BodyAsync(g.W.Stranger, Routes.MeGrades, HttpStatusCode.Forbidden));
        AssertNoLeak(g, await BodyAsync(g.W.OtherTeacher, Routes.MeGrades, HttpStatusCode.Forbidden));
        AssertNoLeak(g, await BodyAsync(g.StrangerGuardian, ChildGrades(g.W.StudentProfileId), HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Staff_preview_response_never_carries_a_hidden_or_scheduled_item()
    {
        var g = await GradeReadsTestKit.NewAsync(factory, _t, "GL6");

        AssertNoLeak(g, await BodyAsync(g.W.Teacher, Routes.ClassCourseGradebookStudentView(g.W.ClassCourse.Id, g.W.StudentProfileId)));
    }
}
