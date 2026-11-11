using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M2 permission and isolation probes (M2 plan Task 34, PRD §4.2.3, FR-SEC-05, §9.2). The M2 rows live with their feature
/// PRs in <c>Security/ProbeRows</c>, and <see cref="M1EndpointMatrixTests"/> probes them with the seven M1 callers. This
/// class sends the same requests as the M2 staff callers (<see cref="M2Caller"/>) and compares with
/// <see cref="M2StaffCallerExpectations"/>. Rows go live by themselves when their route is mapped (pending until then).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M2EndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>Route prefixes owned by M2 (D23: student, guardian and change-request routes arrive in M3).</summary>
    private static readonly string[] M2Prefixes =
        [Routes.Staff, Routes.Imports, Routes.IdentityProviders, "/api/dev/jobs"];

    private static IEnumerable<ProbeRow> M2Rows => M1ProbeRows.All.Where(r => IsM2(r.Pattern));

    public static TheoryData<string, M2Caller> Cases()
    {
        var data = new TheoryData<string, M2Caller>();
        foreach (var row in M2Rows.Where(r => M2StaffCallerExpectations.TryGet(r.Method, r.Pattern, out _)))
        {
            foreach (var caller in Enum.GetValues<M2Caller>())
            {
                data.Add(row.Name, caller);
            }
        }

        // The theory needs at least one case; the hand-written staff-caller guards below do not depend on it.
        if (data.Count == 0)
        {
            data.Add(string.Empty, M2Caller.School1Manager);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Endpoint_answers_the_expected_status_for_the_staff_caller(string rowName, M2Caller caller)
    {
        if (rowName.Length == 0)
        {
            output.WriteLine("No M2 row with staff-caller expectations yet.");
            return;
        }

        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        M2StaffCallerExpectations.TryGet(row.Method, row.Pattern, out var expectations);
        var callers = await M2Callers.GetAsync(factory);
        var request = await row.Build(callers.World, Caller.RootAdmin);
        using var response = await callers.SendAsync(caller, row.Method, request.Url, request.Body);

        var expected = expectations[caller];
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected,
            $"{row.Name}: {row.Method} {request.Url} as {caller} → expected {expected}, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    [Fact]
    public void M2_staff_caller_expectations_are_complete_and_name_real_patterns()
    {
        foreach (var key in M2StaffCallerExpectations.Keys)
        {
            var space = key.IndexOf(' ');
            M2StaffCallerExpectations.TryGet(new HttpMethod(key[..space]), key[(space + 1)..], out var row);
            Assert.All(Enum.GetValues<M2Caller>(), caller => Assert.True(row.ContainsKey(caller), $"{key}: no expectation for {caller}"));
            Assert.True(IsM2(key[(space + 1)..]), $"{key} is not an M2 route");
        }
    }

    [Fact]
    public void Every_mapped_M2_route_has_a_probe_row_and_staff_caller_expectations()
    {
        // Routes without a row are a failure (the M1 coverage guard also catches them); routes with a row but no staff-caller
        // expectation are listed so the BE PR's reviewer can ask for the entry (M2StaffCallerExpectations).
        var rows = M2Rows.Select(r => M2StaffCallerExpectations.Key(r.Method, r.Pattern)).ToHashSet();
        var noRow = ApiRoutes().Where(r => IsM2(r.Pattern))
            .Select(r => Key(r.Method, r.Pattern))
            .Where(k => !rows.Contains(k))
            .Distinct()
            .ToList();
        Assert.True(noRow.Count == 0, "M2 endpoints without a probe row (add one under Security/ProbeRows):\n" + string.Join('\n', noRow));

        var noExpectations = M2Rows.Where(r => IsMapped(r) && !M2StaffCallerExpectations.TryGet(r.Method, r.Pattern, out _))
            .Select(r => $"{r.Method} {r.Pattern} ({r.Name})")
            .ToList();
        output.WriteLine(noExpectations.Count == 0
            ? "Every mapped M2 row has staff-caller expectations."
            : "Mapped M2 rows without staff-caller expectations (add them to M2StaffCallerExpectations):\n" + string.Join('\n', noExpectations));
    }

    [Theory]
    [InlineData(M2Caller.School1Manager, 200)]
    [InlineData(M2Caller.School1Counselor, 403)]
    [InlineData(M2Caller.School1Supervisor, 200)]
    [InlineData(M2Caller.ClientBTeacher, 403)]
    public async Task Staff_callers_hold_valid_contexts_with_their_role_permissions(M2Caller caller, int expected)
    {
        // Guards the callers themselves: a broken context would be 403 "Invalid context" on every row and hide real results.
        var callers = await M2Callers.GetAsync(factory);
        using var response = await callers.SendAsync(caller, HttpMethod.Get, Routes.OrgUnits);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected, $"{caller}: GET {Routes.OrgUnits} → {(int)response.StatusCode}. Body: {Truncate(body)}");
        if (expected == 403)
        {
            Assert.DoesNotContain("Invalid context", body, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Pending_M2_rows_are_listed()
    {
        // Informational: must be empty on main at milestone end (Task 34 step 4).
        var pending = M2Rows.Where(r => !IsMapped(r)).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        output.WriteLine(pending.Count == 0 ? "No pending M2 rows." : "Pending M2 rows:\n" + string.Join('\n', pending));
    }

    [Theory]
    [InlineData("/api/staff", true)]
    [InlineData("/api/staff/{id:guid}/shares/{assignmentId:guid}", true)]
    [InlineData("/api/imports/{kind}/{id:guid}/commit", true)]
    [InlineData("/api/identity-providers/{id:guid}/test", true)]
    [InlineData("/api/dev/jobs/{jobId}/run", true)]
    [InlineData("/api/staffing", false)]
    [InlineData("/api/org-units", false)]
    public void Only_m2_routes_are_in_scope(string pattern, bool m2) => Assert.Equal(m2, IsM2(pattern));

    private static bool IsM2(string pattern) =>
        M2Prefixes.Any(p => pattern.Equals(p, StringComparison.OrdinalIgnoreCase)
                            || pattern.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase));

    private bool IsMapped(ProbeRow row) =>
        ApiRoutes().Any(r => r.Pattern == row.Pattern && string.Equals(r.Method, row.Method.Method, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<(string Method, string Pattern)> ApiRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => (m, e.RoutePattern.RawText!)));

    private static string Key(string method, string pattern) => $"{method.ToUpperInvariant()} {pattern}";

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
