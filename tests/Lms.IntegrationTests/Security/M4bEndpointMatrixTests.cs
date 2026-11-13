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
/// M4b permission probes (M4b plan Task 20, PRD §3.3, FR-SEC-05, D42, D51). The M4b rows live next to the route tables they test
/// (<c>Security/ProbeRows</c>, milestone <see cref="ProbeMilestones.M4b"/>) and <see cref="M1EndpointMatrixTests"/> probes them with
/// the seven M1 callers. This class adds what only M4b needs, with the same callers as M4a (<see cref="M4aCallers"/>: two teachers,
/// three students, a guardian):
/// <list type="bullet">
/// <item>every mapped M4b route has a probe row, declares a permission (the anonymous Zoom webhook excepted) and its rows carry the
/// M4b milestone;</item>
/// <item>a caller whose built-in role lacks the route's permission is refused with 403 (a student on authoring, participants,
/// preview; the guardian on every route). The expectation is derived from the route's <c>RequirePermission</c> and the role's
/// default permissions, so a new M4b route is covered without editing this file.</item>
/// </list>
/// What a role that does hold the permission may reach (own class course, own group, visible module, own attempt) is in the M4b
/// visibility and isolation tests. Rows go live by themselves when their route is mapped.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4bEndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>M4b routes that are deliberately not probed, with the reason.</summary>
    private static readonly Dictionary<string, string> NotProbed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["POST /api/dev/zoom/events"] =
            "Development-only signed webhook trigger of the E2E demo (plan Task 22): mapped only in Development, 404 elsewhere",
    };

    private static IEnumerable<ProbeRow> M4bRows => M1ProbeRows.All.Where(r => M4bRoutes.Is(r.Pattern));

    /// <summary>Rows whose route declares a permission (the anonymous webhook does not).</summary>
    private static IEnumerable<ProbeRow> PermissionRows => M4bRows.Where(r => !M4bRoutes.IsAnonymousByDesign(r.Pattern));

    private static string RoleOf(M4aCaller caller) => caller switch
    {
        M4aCaller.TeacherA or M4aCaller.TeacherB => BuiltInRoles.Teacher,
        M4aCaller.Guardian => BuiltInRoles.Guardian,
        _ => BuiltInRoles.Student,
    };

    public static TheoryData<string, M4aCaller> Cases()
    {
        var data = new TheoryData<string, M4aCaller>();
        foreach (var row in PermissionRows)
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
            output.WriteLine("No M4b row yet.");
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
    public void Every_M4b_api_route_is_in_the_probe_matrix()
    {
        var rows = M1ProbeRows.All.Select(r => $"{r.Method.Method} {r.Pattern}".ToUpperInvariant()).ToHashSet();
        var missing = MappedM4bRoutes()
            .Where(r => !rows.Contains($"{r.Method} {r.Pattern}".ToUpperInvariant()) && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(missing.Count == 0,
            "M4b routes without a probe row (add one in Security/ProbeRows, milestone ProbeMilestones.M4b):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Every_M4b_api_route_requires_a_permission()
    {
        var open = MappedM4bRoutes()
            .Where(r => r.Permission is null && !M4bRoutes.IsAnonymousByDesign(r.Pattern) && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(open.Count == 0, "M4b routes without RequirePermission (D42: explicit permissions):\n" + string.Join('\n', open));
    }

    [Fact]
    public void Every_M4b_probe_row_declares_the_M4b_milestone()
    {
        var wrong = M4bRows.Where(r => r.Milestone != ProbeMilestones.M4b).Select(r => $"{r.Method} {r.Pattern} ({r.Name}) = {r.Milestone}").ToList();
        Assert.True(wrong.Count == 0, "Rows of M4b routes must set Milestone = ProbeMilestones.M4b:\n" + string.Join('\n', wrong));
    }

    /// <summary>
    /// Cross-org / cross-unit isolation as a guard, not a convention (QA3 review of #522): a row on a route bound to a unit-owned object
    /// (an <c>{id}</c> in the pattern) must expect 404 or 403 for the admin of School 2 (another unit of the same client) and for the
    /// admin of client B (another client), and 401 for an anonymous caller, never a 2xx. A feature PR that declares success for them fails
    /// here before the row is even sent. Platform-level rows (<c>upcoming</c>, <c>provider-status</c>) and the webhook have no <c>{id}</c>.
    /// </summary>
    [Fact]
    public void Unit_bound_rows_expect_no_success_for_other_units_clients_and_anonymous()
    {
        var wrong = new List<string>();
        foreach (var row in M4bRows.Where(r => r.Pattern.Contains("{id", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var caller in new[] { Caller.School2Admin, Caller.ClientBAdmin })
            {
                if (!row.Expected.TryGetValue(caller, out var status) || status is not (404 or 403))
                {
                    wrong.Add($"{row.Method} {row.Pattern} ({row.Name}): {caller} expects {(row.Expected.TryGetValue(caller, out var s) ? s.ToString() : "nothing")}, must be 404 or 403");
                }
            }

            if (!row.Expected.TryGetValue(Caller.Anonymous, out var anonymous) || anonymous != 401)
            {
                wrong.Add($"{row.Method} {row.Pattern} ({row.Name}): Anonymous must expect 401");
            }
        }

        Assert.True(wrong.Count == 0, "M4b rows on unit-owned routes must refuse other units and clients:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Every_M4b_row_name_is_unique()
    {
        var duplicates = M4bRows.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate probe row names: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Pending_M4b_rows_are_listed()
    {
        // Informational until M4b ends: M1EndpointMatrixTests lists waiting rows of the milestone in progress and fails them once
        // ProbeMilestones.LastClosed reaches 5.
        var pending = M4bRows.Where(r => !IsMapped(r)).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        output.WriteLine(pending.Count == 0 ? "No pending M4b rows." : "Pending M4b rows:\n" + string.Join('\n', pending));
    }

    [Theory]
    [InlineData("/api/live-sessions/{id:guid}", true)]
    [InlineData("/api/live-sessions/{id:guid}/join", true)]
    [InlineData("/api/live-sessions/upcoming", true)]
    [InlineData("/api/live-sessions/provider-status", true)]
    [InlineData("/api/class-courses/{id:guid}/live-sessions", true)]
    [InlineData("/api/class-courses/{id:guid}/packages", true)]
    [InlineData("/api/packages/{id:guid}/launch", true)]
    [InlineData("/api/package-attempts/{id:guid}/commit", true)]
    [InlineData("/api/integrations/zoom/webhook", true)]
    [InlineData("/api/dev/zoom/events", true)]
    [InlineData("/api/class-courses/{id:guid}/teachers", false)]
    [InlineData("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules", false)] // M4a
    [InlineData("/api/library/resources", false)]
    [InlineData("/api/me/notifications", false)]
    [InlineData("/api/live-sessionsx", false)]
    public void Only_m4b_routes_are_in_scope(string pattern, bool m4b) => Assert.Equal(m4b, M4bRoutes.Is(pattern));

    private IEnumerable<(string Method, string Pattern, string? Permission)> MappedM4bRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && M4bRoutes.Is(raw))
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
