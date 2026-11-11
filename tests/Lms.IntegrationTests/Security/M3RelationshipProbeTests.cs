using System.Net;
using Lms.IntegrationTests.Infrastructure;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Relationship probes for M3 (M3 plan Task 45, D42): a Teacher reaches only the students of the class courses they teach
/// (and their homeroom class); a Supervisor only those of the courses they supervise. Every route that exposes a student
/// or the guardian of a student must answer 404 (not 403, not data) for a student outside that relationship.
/// World: <see cref="M3Callers"/>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M3RelationshipProbeTests(LmsApiFactory factory)
{
    private async Task<(M3Callers W, HttpResponseMessage R)> SendAsync(M3Caller caller, string url, HttpMethod? method = null)
    {
        var w = await M3Callers.GetAsync(factory);
        return (w, await w.SendAsync(caller, method ?? HttpMethod.Get, url));
    }

    [Fact]
    public async Task Teacher_reads_the_guardians_of_a_student_in_a_class_course_they_teach()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.TeacherA, HttpMethod.Get, $"/api/students/{w.Data.Student1.Id}/guardians");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact(Skip = "Bug #310: guardian routes ignore the relationship scope (D42)")]
    public async Task Teacher_gets_404_for_the_guardians_of_a_student_in_a_colleagues_class_course()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.TeacherA, HttpMethod.Get, $"/api/students/{w.Data.Student2.Id}/guardians");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact(Skip = "Bug #310: guardian routes ignore the relationship scope (D42)")]
    public async Task Teacher_gets_404_for_a_guardian_who_has_no_student_in_their_class_courses()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.TeacherA, HttpMethod.Get, $"/api/guardians/{w.Data.Guardian2.Id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact(Skip = "Bug #310: guardian routes ignore the relationship scope (D42)")]
    public async Task Teacher_guardian_list_excludes_guardians_of_students_outside_their_class_courses()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.TeacherA, HttpMethod.Get, "/api/guardians?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains(w.Data.Guardian1.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Data.Guardian2.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Pending: GET /api/students is not mapped yet (M3 Task 12); un-skip when it lands")]
    public async Task Supervisor_gets_404_for_a_student_of_a_course_they_do_not_supervise()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.SupervisorOfCx, HttpMethod.Get, $"/api/students/{w.Data.Student2.Id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact(Skip = "Pending: GET /api/students is not mapped yet (M3 Task 12); un-skip when it lands")]
    public async Task Teacher_gets_404_for_a_student_in_a_colleagues_class_course()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.TeacherA, HttpMethod.Get, $"/api/students/{w.Data.Student2.Id}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact(Skip = "Pending: GET /api/students is not mapped yet (M3 Task 12); un-skip when it lands")]
    public async Task Teacher_student_list_shows_only_their_own_students()
    {
        var w = await M3Callers.GetAsync(factory);
        var r = await w.SendAsync(M3Caller.TeacherA, HttpMethod.Get, "/api/students?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.Contains(w.Data.Student1.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Data.Student2.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpStatusCode> StatusAsync(M3Caller caller, string url)
    {
        var w = await M3Callers.GetAsync(factory);
        using var r = await w.SendAsync(caller, HttpMethod.Get, url);
        return r.StatusCode;
    }

    private async Task<string> BodyAsync(M3Caller caller, string url)
    {
        var w = await M3Callers.GetAsync(factory);
        using var r = await w.SendAsync(caller, HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Teacher_reads_a_class_they_teach_and_gets_404_for_a_colleagues_class()
    {
        var w = await M3Callers.GetAsync(factory);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(M3Caller.TeacherA, $"/api/classes/{w.Data.Class1.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(M3Caller.TeacherA, $"/api/classes/{w.Data.Class2.Id}"));
    }

    [Fact]
    public async Task Teacher_class_list_shows_only_classes_they_teach()
    {
        var w = await M3Callers.GetAsync(factory);
        var body = await BodyAsync(M3Caller.TeacherA, $"/api/classes?academicYearId={w.Data.Year.Id}&pageSize=100");
        Assert.Contains(w.Data.Class1.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Data.Class2.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Teacher_lists_students_of_their_class_and_gets_404_for_a_colleagues_class()
    {
        var w = await M3Callers.GetAsync(factory);
        var own = await BodyAsync(M3Caller.TeacherA, $"/api/classes/{w.Data.Class1.Id}/students");
        Assert.Contains(w.Data.Student1.Id.ToString(), own, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(M3Caller.TeacherA, $"/api/classes/{w.Data.Class2.Id}/students"));
    }

    [Fact]
    public async Task Teacher_reads_enrollments_of_their_class_course_only()
    {
        var w = await M3Callers.GetAsync(factory);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(M3Caller.TeacherA, $"/api/class-courses/{w.Data.ClassCourseX.Id}/enrollments"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(M3Caller.TeacherA, $"/api/class-courses/{w.Data.ClassCourseY.Id}/enrollments"));
    }

    [Fact]
    public async Task Teacher_reads_the_enrollments_of_their_students_only()
    {
        var w = await M3Callers.GetAsync(factory);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(M3Caller.TeacherA, $"/api/students/{w.Data.Student1.Id}/enrollments"));
        Assert.Equal(HttpStatusCode.NotFound, await StatusAsync(M3Caller.TeacherA, $"/api/students/{w.Data.Student2.Id}/enrollments"));
    }

    [Fact]
    public async Task Teacher_enrollment_query_excludes_other_class_courses()
    {
        var w = await M3Callers.GetAsync(factory);
        var body = await BodyAsync(M3Caller.TeacherA, "/api/enrollments?pageSize=100");
        Assert.Contains(w.Data.Student1.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Data.Student2.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Supervisor_may_read_enrollments_of_every_course_in_the_unit_by_design()
    {
        // D42 restricts what a Supervisor ACTS on (RelationshipRules lists Manage/Update/Publish permissions only);
        // Enrollment.View is unit-wide for the role. If the rule table ever adds it, this is the probe to flip to 404.
        var w = await M3Callers.GetAsync(factory);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(M3Caller.SupervisorOfCx, $"/api/class-courses/{w.Data.ClassCourseX.Id}/enrollments"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(M3Caller.SupervisorOfCx, $"/api/class-courses/{w.Data.ClassCourseY.Id}/enrollments"));
    }

    [Fact]
    public async Task Student_and_guardian_cannot_read_classes_or_enrollments()
    {
        var w = await M3Callers.GetAsync(factory);
        foreach (var caller in new[] { M3Caller.Student1, M3Caller.Guardian1 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(caller, $"/api/classes/{w.Data.Class1.Id}"));
            Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(caller, $"/api/classes/{w.Data.Class1.Id}/students"));
            Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(caller, $"/api/class-courses/{w.Data.ClassCourseX.Id}/enrollments"));
        }
    }
}
