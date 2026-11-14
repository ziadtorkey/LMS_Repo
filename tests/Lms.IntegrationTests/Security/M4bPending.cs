using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The pinned set of M4b things the probes wait for (M4b plan Task 20; the same mechanism as <c>M6Pending</c> on #770). A probe that waits for a route or a
/// source file asks <see cref="SkipAsync"/> first: a key that is <b>pinned and not live</b> skips with a visible PENDING line; a key that is <b>not pinned and
/// not live</b> fails (a route landed under another verb or pattern and nothing would probe it); a key that is <b>live while still pinned</b> fails in
/// <see cref="M4bPendingProbeTests"/> until the pin is removed, so the merge of #544, #511, #608, #576 and the package host forces its probes to run.
/// Remove a pin in the PR that makes its key live.
/// </summary>
public static class M4bPending
{
    public const string Join = "ROUTE POST /api/live-sessions/{id:guid}/join";
    public const string Recording = "ROUTE PUT /api/live-sessions/{id:guid}/recording";
    public const string Participants = "ROUTE GET /api/live-sessions/{id:guid}/participants";
    public const string Upcoming = "ROUTE GET /api/live-sessions/upcoming";
    public const string Webhook = "ROUTE POST /api/integrations/zoom/webhook";
    public const string Launch = "ROUTE POST /api/packages/{id:guid}/launch";
    public const string Preview = "ROUTE POST /api/packages/{id:guid}/preview";
    public const string Commit = "ROUTE POST /api/package-attempts/{id:guid}/commit";
    public const string Finish = "ROUTE POST /api/package-attempts/{id:guid}/finish";
    public const string ZoomEventProcessor = "FILE src/Lms.Infrastructure/Zoom/ZoomEventProcessor.cs";

    /// <summary>Key to what unblocks it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Pins = new Dictionary<string, string>
    {
        [Join] = "#544 (live lifecycle)",
        [Recording] = "#544 (live lifecycle)",
        [Participants] = "#544 (live lifecycle)",
        [Upcoming] = "#544 or the upcoming-sessions PR",
        [Webhook] = "#511 (Zoom webhooks)",
        [Launch] = "#576 (package attempts)",
        [Preview] = "#576 (package attempts)",
        [Commit] = "#576 (package attempts)",
        [Finish] = "#576 (package attempts)",
        [ZoomEventProcessor] = "#511 (Zoom webhooks)",
    };

    private static readonly string RepoRoot = FindRoot();

    public static bool IsLive(LmsApiFactory factory, string key)
    {
        var kind = key[..key.IndexOf(' ')];
        var rest = key[(key.IndexOf(' ') + 1)..];
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        switch (kind)
        {
            case "ROUTE":
                var parts = rest.Split(' ', 2);
                return endpoints.Where(e => string.Equals(e.RoutePattern.RawText, parts[1], StringComparison.OrdinalIgnoreCase))
                    .Any(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]).Contains(parts[0], StringComparer.OrdinalIgnoreCase));
            case "ROUTEPREFIX":
                return endpoints.Any(e => e.RoutePattern.RawText is { } raw && raw.StartsWith(rest, StringComparison.OrdinalIgnoreCase));
            case "FILE":
                return File.Exists(Path.Combine(RepoRoot, rest.Replace('/', Path.DirectorySeparatorChar)));
            default:
                throw new ArgumentException("Unknown pin kind: " + key);
        }
    }

    /// <summary>
    /// True when the probe must stop because its key is still pending (written to the test output as PENDING). Fails when the key is neither live nor pinned.
    /// Returns false when the probe must run (the key is live, whether or not it is still pinned: the pin test reports that).
    /// </summary>
    public static bool Skip(LmsApiFactory factory, ITestOutputHelper output, string key)
    {
        if (IsLive(factory, key))
        {
            return false;
        }

        Assert.True(Pins.ContainsKey(key), $"{key} is not live and is not pinned in M4bPending.Pins: nothing would probe it. Pin it with the PR that unblocks it, or fix the route name.");
        output.WriteLine($"PENDING (waits for {Pins[key]}): {key}");
        return true;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("LMS.slnx not found above " + AppContext.BaseDirectory);
    }
}
