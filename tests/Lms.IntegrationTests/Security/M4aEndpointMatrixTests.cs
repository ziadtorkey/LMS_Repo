using System.Net;
using Lms.Api.Infrastructure;
using Lms.Domain.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M4a permission probes (M4a plan Task 29, PRD §3.3, FR-SEC-05, D42). The M4a rows live with their feature PRs
/// (<c>Security/ProbeRows</c>, milestone <see cref="ProbeMilestones.M4a"/>) and <see cref="M1EndpointMatrixTests"/> probes them with
/// the seven M1 callers. This class adds what only M4a needs:
/// <list type="bullet">
/// <item>every mapped M4a route has a probe row, declares a permission and its rows carry the M4a milestone;</item>
/// <item>the callers of <see cref="M4aCallers"/> (two teachers, three students, a guardian) are refused with 403 on every M4a route whose
/// permission their built-in role does not hold: builder and grading routes for students, every route for the guardian (D42, grades
/// arrive in M5). The expectation is derived from the route's <c>RequirePermission</c> and the role's default permissions, so a
/// new M4a route is covered without editing this file.</item>
/// </list>
/// What a role that does hold the permission may reach (own class course, own group, visible module) is in
/// <see cref="M4aVisibilityProbeTests"/>. Rows go live by themselves when their route is mapped.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4aEndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>M4a routes that are deliberately not probed, with the reason.</summary>
    private static readonly Dictionary<string, string> NotProbed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POST /api/dev/jobs/quiz-close/{quizId:guid}"] =
            "Development-only trigger of the E2E exit demo (plan Task 11): mapped only in Development, 404 elsewhere",
    };

    private static IEnumerable<ProbeRow> M4aRows => M1ProbeRows.All.Where(r => M4aRoutes.Is(r.Pattern));

    private static string RoleOf(M4aCaller caller) => caller switch
    {
        M4aCaller.TeacherA or M4aCaller.TeacherB => BuiltInRoles.Teacher,
        M4aCaller.Guardian => BuiltInRoles.Guardian,
        _ => BuiltInRoles.Student,
    };

    public static TheoryData<string, M4aCaller> Cases()
    {
        var data = new TheoryData<string, M4aCaller>();
        foreach (var row in M4aRows)
        {
            foreach (var caller in Enum.GetValues<M4aCaller>())
            {
                data.Add(row.Name, caller);
            }
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty, M4aCaller.StudentGroup1);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Caller_is_refused_with_403_where_the_role_lacks_the_permission(string rowName, M4aCaller caller)
    {
        if (rowName.Length == 0)
        {
            output.WriteLine("No M4a row yet.");
            return;
        }

        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        var permission = PermissionOf(row);
        Assert.True(permission is not null, $"{row.Method} {row.Pattern} declares no RequirePermission.");
        if (BuiltInRoles.Get(RoleOf(caller)).DefaultPermissions.Contains(permission))
        {
            output.WriteLine($"{caller} holds {permission}: answered by the visibility probes, not asserted here.");
            return;
        }

        var w = await M4aCallers.GetAsync(factory);
        var request = await row.Build(w.World, Caller.RootAdmin);
        using var response = await w.SendAsync(caller, row.Method, request.Url, request.Body);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{row.Name}: {row.Method} {request.Url} as {caller} ({RoleOf(caller)} lacks {permission}) → expected 403, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    [Fact]
    public void Every_M4a_api_route_is_in_the_probe_matrix()
    {
        var rows = M1ProbeRows.All.Select(r => $"{r.Method.Method} {r.Pattern}".ToUpperInvariant()).ToHashSet();
        var missing = MappedM4aRoutes()
            .Where(r => !rows.Contains($"{r.Method} {r.Pattern}".ToUpperInvariant()) && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(missing.Count == 0,
            "M4a routes without a probe row (add one in Security/ProbeRows, milestone ProbeMilestones.M4a):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Every_M4a_api_route_requires_a_permission()
    {
        var open = MappedM4aRoutes()
            .Where(r => r.Permission is null && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(open.Count == 0, "M4a routes without RequirePermission (D42: explicit permissions):\n" + string.Join('\n', open));
    }

    [Fact]
    public void Every_M4a_probe_row_declares_the_M4a_milestone()
    {
        var wrong = M4aRows.Where(r => r.Milestone != ProbeMilestones.M4a).Select(r => $"{r.Method} {r.Pattern} ({r.Name}) = {r.Milestone}").ToList();
        Assert.True(wrong.Count == 0, "Rows of M4a routes must set Milestone = ProbeMilestones.M4a:\n" + string.Join('\n', wrong));
    }

    [Fact]
    public void Every_M4a_row_name_is_unique()
    {
        var duplicates = M4aRows.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate probe row names: " + string.Join(", ", duplicates));
    }

    [Theory]
    [InlineData(M4aCaller.TeacherA, 200)]
    [InlineData(M4aCaller.TeacherB, 200)]
    [InlineData(M4aCaller.StudentGroup1, 403)]
    [InlineData(M4aCaller.StudentGroup2, 403)]
    [InlineData(M4aCaller.StudentOfOtherClass, 403)]
    [InlineData(M4aCaller.Guardian, 403)]
    public async Task M4a_callers_hold_valid_contexts_with_their_role_permissions(M4aCaller caller, int expected)
    {
        // Guards the callers themselves: a broken context would be 403 "Invalid context" on every row and hide real results.
        var w = await M4aCallers.GetAsync(factory);
        var url = caller is M4aCaller.TeacherA or M4aCaller.TeacherB ? Routes.Guardians : Routes.OrgUnits;
        using var response = await w.SendAsync(caller, HttpMethod.Get, url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{caller}: GET {url} → {(int)response.StatusCode}. Body: {Truncate(body)}");
        if (expected == 403)
        {
            Assert.DoesNotContain("Invalid context", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task The_M4a_world_has_two_groups_of_X_with_one_student_in_each()
    {
        var w = await M4aCallers.GetAsync(factory);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var groupIds = new[] { w.Data.Group1.Id, w.Data.Group2.Id };
        var members = await db.Set<GroupMember>().Where(m => groupIds.Contains(m.GroupId)).ToListAsync();
        Assert.Equal(2, members.Count);
        Assert.Single(members, m => m.GroupId == w.Data.Group1.Id);
        Assert.Single(members, m => m.GroupId == w.Data.Group2.Id);
        Assert.NotEqual(w.Data.StudentGroup1.Id, w.Data.StudentGroup2.Id);
    }

    [Fact]
    public void Pending_M4a_rows_are_listed()
    {
        // Informational until M4a ends: M1EndpointMatrixTests lists waiting rows of the milestone in progress and fails them once
        // ProbeMilestones.LastClosed reaches 4.
        var pending = M4aRows.Where(r => !IsMapped(r)).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        output.WriteLine(pending.Count == 0 ? "No pending M4a rows." : "Pending M4a rows:\n" + string.Join('\n', pending));
    }

    [Theory]
    [InlineData("/api/modules/{id:guid}", true)]
    [InlineData("/api/modules/{id:guid}/completion", true)]
    [InlineData("/api/assets/{id:guid}/url", true)]
    [InlineData("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules", true)]
    [InlineData("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules/order", true)]
    [InlineData("/api/class-courses/{id:guid}/progress", true)]
    [InlineData("/api/class-courses/{id:guid}/assignments", true)]
    [InlineData("/api/library/resources", true)]
    [InlineData("/api/class-courses/{id:guid}/teachers", false)]
    [InlineData("/api/class-courses/{id:guid}/lesson-plan", false)]
    [InlineData("/api/class-courses/{id:guid}/groups", false)]
    [InlineData("/api/assignments/{id:guid}", false)] // M1 role assignments
    [InlineData("/api/dev/jobs/{jobId}/run", false)]
    [InlineData("/api/dev/jobs/quiz-close/{quizId:guid}", true)]
    [InlineData("/api/me/notifications", false)]
    [InlineData("/api/modulesx", false)]
    public void Only_m4a_routes_are_in_scope(string pattern, bool m4a) => Assert.Equal(m4a, M4aRoutes.Is(pattern));

    private IEnumerable<(string Method, string Pattern, string? Permission)> MappedM4aRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && M4aRoutes.Is(raw))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => (m, e.RoutePattern.RawText!, e.Metadata.GetMetadata<RequiresPermissionMetadata>()?.Permission)));

    private string? PermissionOf(ProbeRow row) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, row.Pattern, StringComparison.OrdinalIgnoreCase)
                        && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                        .Contains(row.Method.Method, StringComparer.OrdinalIgnoreCase))
            .Select(e => e.Metadata.GetMetadata<RequiresPermissionMetadata>()?.Permission)
            .FirstOrDefault();

    private bool IsMapped(ProbeRow row) =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, row.Pattern, StringComparison.OrdinalIgnoreCase))
            .Any(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Contains(row.Method.Method, StringComparer.OrdinalIgnoreCase));

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
