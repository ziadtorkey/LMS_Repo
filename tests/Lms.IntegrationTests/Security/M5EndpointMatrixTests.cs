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
/// M5 permission and isolation probes (M5 plan #621 Task 40, PRD §4.2.3, FR-SEC-05, D42, D58 to D61): the scaffold, ahead of the routes. The
/// rows live next to the route tables they test (<c>Security/ProbeRows/M5*ProbeRows.cs</c>, milestone <see cref="ProbeMilestones.M5"/>) and
/// <see cref="M1EndpointMatrixTests"/> probes them with the seven M1 callers once the route is mapped. A row whose route is not mapped yet
/// is pending and must say which wave, contract and BE task adds it (<see cref="ProbeRow.Blocker"/>); it goes live by itself when the
/// route is mapped, so a contract PR needs no edit here. This class adds what only M5 needs:
/// <list type="bullet">
/// <item>every mapped M5 route has a probe row, declares a permission and its rows carry the M5 milestone;</item>
/// <item>every pending row names its blocker, and every route family of the plan has at least one row (so a row cannot be dropped quietly);</item>
/// <item>a caller whose built-in role lacks the route's permission is refused with 403, derived from the route's <c>RequirePermission</c> and the
/// role defaults, for the M4a callers (teachers, students, guardian) and the M2 callers (Manager, Counselor, Supervisor, a teacher of another
/// client), so "a Counselor cannot read grades" holds without editing this file;</item>
/// <item>rows on unit-bound routes expect 404 or 403, never success, for another unit's admin, another client's admin and an anonymous caller.</item>
/// </list>
/// What a role that holds the permission may reach (own class course, assignment dates, substitute date, guardian link, published-only) is in the
/// M5 relationship, isolation and leak tests of the same task (Task 40 and Task 41).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M5EndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>M5 routes that are deliberately not probed, with the reason.</summary>
    private static readonly Dictionary<string, string> NotProbed = new(StringComparer.OrdinalIgnoreCase)
    {
        // Nothing yet: every M5 route is a permissioned API route of the plan's route table.
    };

    private static IEnumerable<ProbeRow> M5Rows => M1ProbeRows.All.Where(r => M5Routes.Is(r.Pattern));

    /// <summary>Rows whose route declares a permission (every M5 route does: there is no anonymous M5 route).</summary>
    private static IEnumerable<ProbeRow> PermissionRows => M5Rows;

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
            output.WriteLine("No M5 row yet.");
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
    public void Every_M5_api_route_is_in_the_probe_matrix()
    {
        var rows = M1ProbeRows.All.Select(r => $"{r.Method.Method} {r.Pattern}".ToUpperInvariant()).ToHashSet();
        var missing = MappedM5Routes()
            .Where(r => !rows.Contains($"{r.Method} {r.Pattern}".ToUpperInvariant()) && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(missing.Count == 0,
            "M5 routes without a probe row (add one in Security/ProbeRows, milestone ProbeMilestones.M5):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void Every_M5_api_route_requires_a_permission()
    {
        var open = MappedM5Routes()
            .Where(r => r.Permission is null && !NotProbed.ContainsKey($"{r.Method} {r.Pattern}"))
            .Select(r => $"{r.Method} {r.Pattern}").Distinct().ToList();
        Assert.True(open.Count == 0, "M5 routes without RequirePermission (D42: explicit permissions):\n" + string.Join('\n', open));
    }

    [Fact]
    public void Every_M5_probe_row_declares_the_M5_milestone()
    {
        var wrong = M5Rows.Where(r => r.Milestone != ProbeMilestones.M5).Select(r => $"{r.Method} {r.Pattern} ({r.Name}) = {r.Milestone}").ToList();
        Assert.True(wrong.Count == 0, "Rows of M5 routes must set Milestone = ProbeMilestones.M5:\n" + string.Join('\n', wrong));
    }

    /// <summary>
    /// Cross-org / cross-unit isolation as a guard, not a convention (QA3 review of #522): a row on a route bound to a unit-owned object
    /// (an <c>{id}</c> in the pattern) must expect 404 or 403 for the admin of School 2 (another unit of the same client) and for the
    /// admin of client B (another client), and 401 for an anonymous caller, never a 2xx. A feature PR that declares success for them fails
    /// here before the row is even sent. Rows without an id in the pattern (<c>/api/me/*</c>, <c>/api/attendance/sessions</c>, <c>/api/attendance/alerts</c>, <c>/api/excuses/review</c>) are lists filtered by the caller and are not covered by this guard.
    /// </summary>
    [Fact]
    public void Unit_bound_rows_expect_no_success_for_other_units_clients_and_anonymous()
    {
        var wrong = new List<string>();
        foreach (var row in M5Rows.Where(r => r.Pattern.Contains("{id", StringComparison.OrdinalIgnoreCase)))
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

        Assert.True(wrong.Count == 0, "M5 rows on unit-owned routes must refuse other units and clients:\n" + string.Join("\n", wrong));
    }

    [Fact]
    public void Every_M5_row_name_is_unique()
    {
        var duplicates = M5Rows.GroupBy(r => r.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate probe row names: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Pending_M5_rows_are_listed()
    {
        // Informational until M5 ends: M1EndpointMatrixTests lists waiting rows of the milestone in progress and fails them once
        // ProbeMilestones.LastClosed reaches 6.
        var pending = M5Rows.Where(r => !IsMapped(r)).Select(r => $"{r.Method} {r.Pattern} ({r.Name}): {r.Blocker}").ToList();
        output.WriteLine(pending.Count == 0 ? "No pending M5 rows." : $"Pending M5 rows ({pending.Count}):\n" +string.Join('\n', pending));
    }

    /// <summary>The permission table (<see cref="M5PermissionTable"/>) and the probe rows describe the same routes: none missing, none stale.</summary>
    [Fact]
    public void The_planned_permission_table_and_the_probe_rows_list_the_same_routes()
    {
        var rows = M5Rows.Select(r => $"{r.Method.Method} {r.Pattern}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var table = M5PermissionTable.Planned.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var noPermission = rows.Except(table).OrderBy(x => x).ToList();
        var noRow = table.Except(rows).OrderBy(x => x).ToList();
        Assert.True(noPermission.Count == 0 && noRow.Count == 0,
            "Rows without a planned permission:\n" + string.Join('\n', noPermission) + "\nPlanned permissions without a row:\n" + string.Join('\n', noRow));
    }

    /// <summary>
    /// A mapped M5 route declares one of the permissions its plan row allows (the contract's "Grades.Manage or Grades.View" lists both). A route
    /// guarded by the wrong permission is the commonest authorization defect: a Counselor reaching a gradebook through a View permission, a
    /// student through a Manage one. Pending rows are skipped; the route-less case is the "requires a permission" test above.
    /// </summary>
    [Fact]
    public void Mapped_M5_routes_require_the_planned_permission()
    {
        var wrong = MappedM5Routes()
            .Where(r => r.Permission is not null && M5PermissionTable.Planned.TryGetValue($"{r.Method} {r.Pattern}", out var allowed)
                        && !allowed.Contains(r.Permission, StringComparer.Ordinal))
            .Select(r => $"{r.Method} {r.Pattern}: declares {r.Permission}, the plan allows {string.Join(" or ", M5PermissionTable.Planned[$"{r.Method} {r.Pattern}"])}")
            .Distinct().ToList();
        Assert.True(wrong.Count == 0, "M5 routes guarded by a permission other than the planned one:\n" + string.Join('\n', wrong));
    }

    [Fact]
    public void Every_pending_M5_row_names_its_wave_contract_and_task()
    {
        var vague = M5Rows.Where(r => !IsMapped(r))
            .Where(r => string.IsNullOrWhiteSpace(r.Blocker)
                        || !r.Blocker.Contains("wave", StringComparison.OrdinalIgnoreCase)
                        || !r.Blocker.Contains("Task", StringComparison.Ordinal))
            .Select(r => $"{r.Method} {r.Pattern} ({r.Name}): '{r.Blocker}'").ToList();
        Assert.True(vague.Count == 0,
            "Pending M5 rows must say which wave contract and which BE task add the route (ProbeRow.Blocker, for example \"wave 1 contract (Task 1) + Task 8\"):\n" + string.Join('\n', vague));
    }

    [Fact]
    public void A_live_M5_row_no_longer_carries_a_blocker()
    {
        var stale = M5Rows.Where(r => IsMapped(r) && !string.IsNullOrWhiteSpace(r.Blocker)).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        Assert.True(stale.Count == 0, "The route is mapped: drop the Blocker of these rows so they read as live:\n" + string.Join('\n', stale));
    }

    /// <summary>The route families of the M5 plan table (#621): each must keep at least one probe row, so a row cannot be dropped quietly.</summary>
    private static readonly string[] PlannedFamilies =
    [
        "/api/org-units/{id:guid}/grading-scales", "/api/org-units/{id:guid}/weight-templates", "/api/class-courses/{id:guid}/gradebook",
        "/api/me/grades", "/api/students/{id:guid}/grades", "/api/grade-reports", "/api/academic-years/{id:guid}/term-structure",
        "/api/org-units/{id:guid}/periods", "/api/classes/{id:guid}/timetable", "/api/timetable-slots", "/api/me/timetable",
        "/api/students/{id:guid}/timetable", "/api/attendance/sessions", "/api/class-courses/{id:guid}/attendance-sessions",
        "/api/attendance-sessions", "/api/attendance-reports", "/api/me/attendance", "/api/students/{id:guid}/attendance",
        "/api/students/{id:guid}/excuse-policy", "/api/excuses", "/api/me/excuses", "/api/attendance/alerts",
    ];

    [Fact]
    public void Every_planned_M5_route_family_has_a_probe_row()
    {
        var patterns = M5Rows.Select(r => r.Pattern).ToList();
        var missing = PlannedFamilies.Where(f => !patterns.Any(p => p.StartsWith(f, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(missing.Count == 0, "Route families of the M5 plan without a probe row:\n" + string.Join('\n', missing));
    }

    private static string RoleOf(M2Caller caller) => caller switch
    {
        M2Caller.School1Manager => BuiltInRoles.Manager,
        M2Caller.School1Counselor => BuiltInRoles.Counselor,
        M2Caller.School1Supervisor => BuiltInRoles.Supervisor,
        _ => BuiltInRoles.Teacher, // ClientBTeacher
    };

    public static TheoryData<string, M2Caller> StaffCases()
    {
        var data = new TheoryData<string, M2Caller>();
        foreach (var row in PermissionRows)
        {
            foreach (var caller in Enum.GetValues<M2Caller>())
            {
                data.Add(row.Name, caller);
            }
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty, M2Caller.School1Counselor);
        }

        return data;
    }

    /// <summary>
    /// Counselor, Supervisor, Manager and a teacher of another client: where the role lacks the route's permission the answer is 403 (a Counselor
    /// never reaches a gradebook or a grade report, a Supervisor never writes, L16 of the plan). Roles that hold the permission are answered by the
    /// relationship probes (Task 40), not asserted here.
    /// </summary>
    [Theory]
    [MemberData(nameof(StaffCases))]
    public async Task Staff_caller_is_refused_with_403_where_the_role_lacks_the_permission(string rowName, M2Caller caller)
    {
        if (rowName.Length == 0)
        {
            output.WriteLine("No M5 row yet.");
            return;
        }

        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}: {row.Blocker}");
            return;
        }

        var permission = PermissionOf(row);
        Assert.True(permission is not null, $"{row.Method} {row.Pattern} declares no RequirePermission.");
        if (BuiltInRoles.Get(RoleOf(caller)).DefaultPermissions.Contains(permission))
        {
            output.WriteLine($"{caller} holds {permission}: answered by the relationship probes, not asserted here.");
            return;
        }

        var w = await M2Callers.GetAsync(factory);
        var request = await row.Build(w.World, Caller.RootAdmin);
        using var response = await w.SendAsync(caller, row.Method, request.Url, request.Body);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            $"{row.Name}: {row.Method} {request.Url} as {caller} ({RoleOf(caller)} lacks {permission}) → expected 403, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    [Theory]
    [InlineData("/api/org-units/{id:guid}/grading-scales", true)]
    [InlineData("/api/org-units/{id:guid}/weight-templates/{tid:guid}", true)]
    [InlineData("/api/org-units/{id:guid}/periods", true)]
    [InlineData("/api/class-courses/{id:guid}/gradebook", true)]
    [InlineData("/api/class-courses/{id:guid}/attendance-sessions", true)]
    [InlineData("/api/classes/{id:guid}/timetable", true)]
    [InlineData("/api/academic-years/{id:guid}/term-structure", true)]
    [InlineData("/api/students/{id:guid}/grades", true)]
    [InlineData("/api/students/{id:guid}/excusable-sessions", true)]
    [InlineData("/api/me/grades", true)]
    [InlineData("/api/me/excuses", true)]
    [InlineData("/api/grade-reports/{kind}", true)]
    [InlineData("/api/timetable-slots/{id:guid}/exceptions", true)]
    [InlineData("/api/attendance/sessions", true)]
    [InlineData("/api/attendance-sessions/{id:guid}/records", true)]
    [InlineData("/api/attendance-reports/{kind}", true)]
    [InlineData("/api/excuses/review", true)]
    [InlineData("/api/org-units/{id:guid}/settings/{name}", false)] // M1 settings
    [InlineData("/api/students/{id:guid}", false)] // M3
    [InlineData("/api/classes/{id:guid}", false)] // M3
    [InlineData("/api/class-courses/{id:guid}/live-sessions", false)] // M4b
    [InlineData("/api/class-courses/{id:guid}/lessons/{lessonKey:guid}/modules", false)] // M4a
    [InlineData("/api/me/notifications", false)]
    [InlineData("/api/attendancex", false)]
    public void Only_m5_routes_are_in_scope(string pattern, bool m4b) => Assert.Equal(m4b, M5Routes.Is(pattern));

    private IEnumerable<(string Method, string Pattern, string? Permission)> MappedM5Routes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && M5Routes.Is(raw))
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
