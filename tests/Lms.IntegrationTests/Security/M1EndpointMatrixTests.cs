using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M1 permission and isolation probes (M1 plan Task 35, PRD §4.2, FR-SEC-05): every M1 endpoint is called by every caller
/// and must answer exactly the expected status. A sibling unit or another client is 404 (never 403, never data); a missing
/// permission is 403; client-wide writes below the client root are 403; anonymous is 401.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M1EndpointMatrixTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>API routes that are not M1 (or are self-scoped under /api/me) and therefore not in the matrix.</summary>
    private static readonly HashSet<string> OutOfScope =
    [
        "/api/{**path}",                // 404 fallback
        "/api/auth/whoami",             // M0 identity
        "/api/files",                   // M0 files (own probes in FileEndpointTests)
        "/api/files/{id:guid}/url",
    ];

    public static TheoryData<string, Caller> Cases()
    {
        var data = new TheoryData<string, Caller>();
        foreach (var row in M1ProbeRows.All)
        {
            foreach (var caller in Enum.GetValues<Caller>())
            {
                data.Add(row.Name, caller);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Endpoint_answers_the_expected_status_for_the_caller(string rowName, Caller caller)
    {
        var row = M1ProbeRows.All.Single(r => r.Name == rowName);
        if (!IsMapped(row))
        {
            // Not merged yet: the row goes live by itself when the feature lands (see Pending_rows_are_listed).
            output.WriteLine($"PENDING (route not mapped yet): {row.Method} {row.Pattern}");
            return;
        }

        var world = await M1ProbeWorld.GetAsync(factory);
        var request = await row.Build(world, caller);
        using var response = await world.Callers.SendAsync(caller, row.Method, request.Url, request.Body);

        var expected = row.Expected[caller];
        var body = await response.Content.ReadAsStringAsync();
        Assert.True((int)response.StatusCode == expected,
            $"{row.Name}: {row.Method} {request.Url} as {caller} → expected {expected}, got {(int)response.StatusCode}. Body: {Truncate(body)}");
    }

    [Fact]
    public void Every_M1_api_route_is_in_the_probe_matrix()
    {
        var inMatrix = M1ProbeRows.All.Select(r => Key(r.Method.Method, r.Pattern)).ToHashSet();
        var missing = ApiRoutes()
            .Where(r => !OutOfScope.Contains(r.Pattern) && !IsSelfScoped(r.Pattern))
            .Select(r => Key(r.Method, r.Pattern))
            .Where(key => !inMatrix.Contains(key))
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0,
            "M1 endpoints without probe rows (add them to M1ProbeRows with the expected status per caller):\n"
            + string.Join('\n', missing));
    }

    [Fact]
    public void Matrix_rows_are_unique_and_complete()
    {
        Assert.Equal(M1ProbeRows.All.Count, M1ProbeRows.All.Select(r => r.Name).Distinct().Count());
        foreach (var row in M1ProbeRows.All)
        {
            Assert.All(Enum.GetValues<Caller>(), caller => Assert.True(row.Expected.ContainsKey(caller), $"{row.Name}: no expectation for {caller}"));
        }
    }

    [Fact]
    public void Every_probe_rows_file_is_a_collected_module()
    {
        // A module class that forgets IProbeRowModule would compile but drop its rows silently.
        var notModules = M1ProbeRows.NamedLikeModules.Where(t => !typeof(IProbeRowModule).IsAssignableFrom(t)).Select(t => t.Name);
        Assert.Empty(notModules);
        Assert.NotEmpty(M1ProbeRows.Modules);
    }

    [Fact]
    public void No_row_of_a_closed_milestone_is_pending()
    {
        // M1 and M2 are closed: every row must be live (its route mapped), or the feature it probes is missing or renamed.
        // Rows of the milestone in progress (ProbeRow.Milestone = 3) may wait for their route and are only listed.
        var pending = M1ProbeRows.All.Where(r => !IsMapped(r)).ToList();
        var waiting = pending.Where(r => r.Milestone >= 3).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        output.WriteLine(waiting.Count == 0 ? "No rows of the milestone in progress are waiting." : "Waiting rows (milestone in progress):\n" + string.Join('\n', waiting));

        var stale = pending.Where(r => r.Milestone <= 2).Select(r => $"{r.Method} {r.Pattern} ({r.Name})").ToList();
        Assert.True(stale.Count == 0, "Probe rows of a closed milestone whose route is not mapped:\n" + string.Join('\n', stale));
    }

    private bool IsMapped(ProbeRow row) =>
        ApiRoutes().Any(r => r.Pattern == row.Pattern && string.Equals(r.Method, row.Method.Method, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<(string Method, string Pattern)> ApiRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(m => (m, GenericKind(e.RoutePattern.RawText!))));

    /// <summary>
    /// The caller's own data: exactly <c>/api/me</c> or anything under <c>/api/me/</c>. A plain prefix match would also
    /// exempt unrelated routes such as <c>/api/members</c>.
    /// </summary>
    internal static bool IsSelfScoped(string pattern) =>
        string.Equals(pattern, "/api/me", StringComparison.OrdinalIgnoreCase)
        || pattern.StartsWith("/api/me/", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData("/api/me", true)]
    [InlineData("/api/me/notifications", true)]
    [InlineData("/api/me/notifications/{id:guid}/read", true)]
    [InlineData("/api/members", false)]
    [InlineData("/api/messages", false)]
    [InlineData("/api/metrics/{id:guid}", false)]
    public void Only_api_me_and_routes_below_it_are_exempt(string pattern, bool exempt) =>
        Assert.Equal(exempt, IsSelfScoped(pattern));

    /// <summary>
    /// Import routes are mapped once per kind (<c>/api/imports/staff/...</c>); the matrix describes them by the generic
    /// <c>/api/imports/{kind}/...</c> pattern, because the rules are the framework's and every kind shares them.
    /// </summary>
    private static string GenericKind(string pattern) =>
        System.Text.RegularExpressions.Regex.Replace(pattern, @"^/api/imports/(?!\{)[A-Za-z0-9-]+", "/api/imports/{kind}");

    private static string Key(string method, string pattern) => $"{method.ToUpperInvariant()} {pattern}";

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
