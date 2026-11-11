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
}
