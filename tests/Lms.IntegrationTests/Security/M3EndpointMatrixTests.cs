using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M3 permission probes (M3 plan Task 45, PRD §3.3, FR-SEC-05). The M3 rows live with their feature PRs
/// (<c>Security/ProbeRows</c>) and <see cref="M1EndpointMatrixTests"/> probes them with the seven M1 callers and checks that
/// every mapped route has a row. This class adds the callers that M3 introduces and that hold no management permission at all:
/// the Student and the Guardian of the M3 world (<see cref="M3Callers"/>). Both must be refused (403, never data) on every
/// management route of the M3 modules; what they may read is served by <c>/api/me</c> (out of scope here, D26/D29).
/// What depends on a relationship (a teacher's own class courses, a supervisor's courses) is in
/// <see cref="M3RelationshipProbeTests"/>. Rows go live by themselves when their route is mapped (milestone 3 rows may wait).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M3EndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>Route prefixes owned by M3 (people, structure, enrollment and teaching; D23).</summary>
    private static readonly string[] M3Prefixes =
    [
        Routes.Students, Routes.Guardians, Routes.Classes, Routes.ClassCourses, Routes.Courses, Routes.CourseAllocations,
        Routes.Enrollments, Routes.EnrollmentExceptions, Routes.Groups, Routes.StudentChangeRequests, "/api/teacher-assignments",
        Routes.TeacherEligibility, Routes.Transfers,
    ];

    /// <summary>GET routes under an M3 prefix that the Student role may read through a relationship (own enrollment).</summary>
    private static readonly HashSet<string> StudentReadable =
    [
        Routes.ClassCourseLessonPlanPattern, Routes.ClassCourseLessonModulesPattern, Routes.ClassCourseProgressPattern, Routes.ClassCourseLiveSessionsPattern,
        Routes.LiveSessionPattern, Routes.ClassCourseAssignmentsPattern,
        Routes.ClassCourseQuizzesPattern,
    ];

    private static IEnumerable<ProbeRow> M3Rows => M1ProbeRows.All.Where(r => IsM3(r.Pattern));

    public static TheoryData<string, M3Caller> Cases()
    {
        var data = new TheoryData<string, M3Caller>();
        foreach (var row in M3Rows)
        {
            data.Add(row.Name, M3Caller.Student1);
            data.Add(row.Name, M3Caller.Guardian1);
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty, M3Caller.Student1);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Student_and_guardian_are_refused_on_every_management_route(string rowName, M3Caller caller)
    {
        if (rowName.Length == 0)
        {
            output.WriteLine("No M3 row yet.");
            return;
        }

        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        var w = await M3Callers.GetAsync(factory);
        var request = await row.Build(w.World, Caller.RootAdmin);
        using var response = await w.SendAsync(caller, row.Method, request.Url, request.Body);
        var body = await response.Content.ReadAsStringAsync();
        // A route a student may read (M4a: the plan tree, the modules of a lesson, their own progress) holds the permission for the
        // Student role; the unenrolled student of this world reaches no class course, so it is 404, never data.
        var expected = caller == M3Caller.Student1 && row.Method == HttpMethod.Get && StudentReadable.Contains(row.Pattern)
            ? System.Net.HttpStatusCode.NotFound
            : System.Net.HttpStatusCode.Forbidden;
        Assert.True(response.StatusCode == expected,
            $"{row.Name}: {row.Method} {request.Url} as {caller} → expected {(int)expected}, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    [Theory]
    [InlineData(M3Caller.TeacherA, 200)]
    [InlineData(M3Caller.TeacherB, 200)]
    [InlineData(M3Caller.SupervisorOfCx, 200)]
    [InlineData(M3Caller.SupervisorOfCy, 200)]
    [InlineData(M3Caller.Student1, 403)]
    [InlineData(M3Caller.Guardian1, 403)]
    public async Task M3_callers_hold_valid_contexts_with_their_role_permissions(M3Caller caller, int expected)
    {
        // Guards the callers themselves: a broken context would be 403 "Invalid context" on every row and hide real results.
        var w = await M3Callers.GetAsync(factory);
        // A Teacher holds Students.View but not OrgUnits.View, so each caller is checked against a route its role can reach.
        var url = caller is M3Caller.TeacherA or M3Caller.TeacherB ? Routes.Guardians : Routes.OrgUnits;
        using var response = await w.SendAsync(caller, HttpMethod.Get, url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{caller}: GET {url} → {(int)response.StatusCode}. Body: {Truncate(body)}");
        if (expected == 403)
        {
            Assert.DoesNotContain("Invalid context", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Pending_M3_rows_are_listed()
    {
        // Informational until M3 ends; M1EndpointMatrixTests.No_row_of_a_closed_milestone_is_pending fails for milestone 3 rows
        // once the milestone is closed (raise ProbeRow.Milestone's closed range in that test).
        var pending = M3Rows.Where(r => !IsMapped(r)).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        output.WriteLine(pending.Count == 0 ? "No pending M3 rows." : "Pending M3 rows:\n" + string.Join('\n', pending));
    }

    [Theory]
    [InlineData("/api/students", true)]
    [InlineData("/api/students/{id:guid}/guardians/{guardianId:guid}", true)]
    [InlineData("/api/class-courses/{id:guid}/teachers", true)]
    [InlineData("/api/teacher-eligibility", true)]
    [InlineData("/api/me/profile", false)]
    [InlineData("/api/staff", false)]
    [InlineData("/api/studentsx", false)]
    [InlineData("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules", false)] // M4a: a Student reads modules
    [InlineData("/api/class-courses/{id:guid}/progress", false)]
    public void Only_m3_routes_are_in_scope(string pattern, bool m3) => Assert.Equal(m3, IsM3(pattern));

    private static bool IsM3(string pattern) =>
        !M4aRoutes.Is(pattern)
        && M3Prefixes.Any(p => pattern.Equals(p, StringComparison.OrdinalIgnoreCase)
                               || pattern.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

    private bool IsMapped(ProbeRow row) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, row.Pattern, StringComparison.OrdinalIgnoreCase))
            .Any(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Contains(row.Method.Method, StringComparer.OrdinalIgnoreCase));

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
