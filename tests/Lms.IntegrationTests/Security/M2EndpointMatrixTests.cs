using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M2 permission and isolation probes (M2 plan Task 34, PRD §4.2.3, FR-SEC-05, §9.2). The M2 rows live in
/// <c>Security/ProbeRows</c> next to the M1 rows, so <see cref="M1EndpointMatrixTests"/> already probes them with the
/// seven M1 callers; this class adds the M2 staff callers (<see cref="M2Caller"/>) and requires every M2 route to carry
/// expectations for them. Rows go live by themselves when their route is mapped (pending until then).
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
        foreach (var row in M2Rows)
        {
            foreach (var caller in Enum.GetValues<M2Caller>())
            {
                data.Add(row.Name, caller);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Endpoint_answers_the_expected_status_for_the_staff_caller(string rowName, M2Caller caller)
    {
        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        var callers = await M2Callers.GetAsync(factory);
        var request = await row.Build(callers.World, Caller.RootAdmin);
        using var response = await callers.SendAsync(caller, row.Method, request.Url, request.Body);

        var expected = row.M2Expected![caller];
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected,
            $"{row.Name}: {row.Method} {request.Url} as {caller} → expected {expected}, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    [Fact]
    public void Every_M2_api_route_is_in_the_probe_matrix_with_staff_caller_expectations()
    {
        var withExpectations = M2Rows.Where(r => r.M2Expected is not null).Select(r => Key(r.Method.Method, r.Pattern)).ToHashSet();
        var missing = ApiRoutes()
            .Where(r => IsM2(r.Pattern))
            .Select(r => Key(r.Method, r.Pattern))
            .Where(key => !withExpectations.Contains(key))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0,
            "M2 endpoints without probe rows or without M2Expected (add them under Security/ProbeRows):\n" + string.Join('\n', missing));
    }

    [Fact]
    public void M2_rows_have_an_expectation_for_every_staff_caller()
    {
        Assert.NotEmpty(M2Rows);
        foreach (var row in M2Rows)
        {
            Assert.True(row.M2Expected is not null, $"{row.Name}: no M2Expected");
            Assert.All(Enum.GetValues<M2Caller>(), caller =>
                Assert.True(row.M2Expected!.ContainsKey(caller), $"{row.Name}: no expectation for {caller}"));
        }
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
    public void Only_m2_routes_need_staff_caller_expectations(string pattern, bool m2) => Assert.Equal(m2, IsM2(pattern));

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
