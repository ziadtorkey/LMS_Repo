using System.Diagnostics;
using System.Net;
using System.Text;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Grading;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Perf;

/// <summary>
/// M7 X-09 load harness: the key read paths of a 50,000-student client at p95 under concurrent users, against a database seeded by the performance seed
/// (<c>Seed:Perf:Students=50000</c>, see docs/runbook-performance.md). It runs only when <c>LMS_PERF_CONNECTION</c> names that database, so CI and ordinary
/// runs skip it. The host is in-process with test authentication (no tokens), the database is real MySQL: what is measured is the application and its queries.
/// Targets: PRD X-09 is "list pages under 2 s at p95 with 50,000 students per client"; the same budget is applied to every path until the UX review sets
/// stricter ones. Paths whose endpoints are not on this branch yet (my grades and the gradebook grid of M5, attendance today, timetable, messages inbox) are listed as pending and run with
/// <c>LMS_PERF_PENDING=1</c> once their milestones are merged.
/// </summary>
public sealed class PerfHarnessTests(ITestOutputHelper output)
{
    public const double P95TargetMs = 2000;

    private static int Env(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    private sealed class PerfFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Seed:RunOnStartup", "false");
            builder.UseSetting("Jobs:ServerEnabled", "false");
            builder.UseSetting("ConnectionStrings:Lms", connectionString);
            builder.UseSetting("Auth:WasmOrigins:0", "http://localhost");
            builder.ConfigureServices(services => services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
        }
    }

    private sealed record Caller(Guid UserId, Guid AssignmentId);

    private sealed record Scenario(string Name, string Who, Func<World, string> Url, bool Pending = false);

    private sealed record World(Guid ClassId, Guid ClassCourseId, Guid TermId, Guid UnitId);

    private static readonly Scenario[] Scenarios =
    [
        new("home: my permissions", "student", _ => Routes.MyPermissions),
        new("home: my grades (student)", "student", _ => Routes.MeGrades, Pending: true), // M5 grading endpoints
        new("student list, page 1", "admin", _ => $"{Routes.Students}?page=1&pageSize=50"),
        new("student list, search + sort", "admin", _ => $"{Routes.Students}?search=Student%201234&sort=name&page=1&pageSize=50"),
        new("student list, deep page", "admin", _ => $"{Routes.Students}?page=400&pageSize=50"),
        new("class list", "admin", _ => $"{Routes.Classes}?pageSize=50"),
        new("class students", "teacher", w => Routes.ClassStudents(w.ClassId)),
        new("gradebook grid", "teacher", w => Routes.ClassCourseGradebook(w.ClassCourseId, w.TermId), Pending: true), // M5 grading endpoints
        new("inbox: notifications", "student", _ => Routes.MyNotifications),
        new("attendance today", "teacher", w => $"/api/classes/{w.ClassId}/attendance/today", Pending: true),
        new("timetable (class, term)", "teacher", w => $"/api/classes/{w.ClassId}/timetable?termId={w.TermId}", Pending: true),
        new("messages inbox", "student", _ => "/api/conversations", Pending: true),
    ];

    [Fact]
    public async Task Key_read_paths_meet_the_p95_target_on_the_perf_client()
    {
        if (Environment.GetEnvironmentVariable("LMS_PERF_CONNECTION") is not { Length: > 0 } connection)
        {
            return; // not asked for: the harness needs the seeded database
        }

        var users = Env("LMS_PERF_USERS", 20);
        var iterations = Env("LMS_PERF_ITERATIONS", 200);
        var includePending = Environment.GetEnvironmentVariable("LMS_PERF_PENDING") == "1";

        await using var factory = new PerfFactory(connection);
        var (callers, world) = await LoadWorldAsync(connection);
        using var warm = factory.CreateClient();
        var report = new StringBuilder($"| Path | Who | Requests | p50 ms | p95 ms | p99 ms | Errors | Target p95 | Result |\n|---|---|---|---|---|---|---|---|---|\n");
        var failures = new List<string>();

        foreach (var scenario in Scenarios.Where(s => !s.Pending || includePending))
        {
            var url = scenario.Url(world);
            var caller = callers[scenario.Who];
            await SendAsync(warm, url, caller); // warm-up: JIT, query plans, connection pool
            var timings = new System.Collections.Concurrent.ConcurrentBag<(double Ms, bool Ok)>();
            var statuses = new System.Collections.Concurrent.ConcurrentBag<HttpStatusCode>();
            var next = 0;
            await Task.WhenAll(Enumerable.Range(0, users).Select(_ => Task.Run(async () =>
            {
                using var client = factory.CreateClient();
                while (Interlocked.Increment(ref next) <= iterations)
                {
                    var watch = Stopwatch.StartNew();
                    var status = await SendAsync(client, url, caller);
                    timings.Add((watch.Elapsed.TotalMilliseconds, status == HttpStatusCode.OK));
                    if (status != HttpStatusCode.OK)
                    {
                        statuses.Add(status);
                    }
                }
            })));

            var sorted = timings.Select(t => t.Ms).Order().ToArray();
            var errors = timings.Count(t => !t.Ok);
            double Percentile(double p) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
            var pass = errors == 0 && Percentile(0.95) <= P95TargetMs;
            report.AppendLine($"| {scenario.Name} | {scenario.Who} | {sorted.Length} | {Percentile(0.5):F0} | {Percentile(0.95):F0} | {Percentile(0.99):F0} | {errors} | {P95TargetMs:F0} | {(pass ? "pass" : "FAIL")} |");
            if (!pass)
            {
                failures.Add($"{scenario.Name}: p95 {Percentile(0.95):F0} ms, {errors} errors{(errors > 0 ? $" (first status {statuses.First()}: {LastBody})" : string.Empty)}");
            }
        }

        var text = report.ToString();
        output.WriteLine(text);
        if (Environment.GetEnvironmentVariable("LMS_PERF_REPORT") is { Length: > 0 } path)
        {
            await File.WriteAllTextAsync(path, $"# Performance run ({users} concurrent users, {iterations} requests per path)\n\n{text}");
        }

        Assert.True(failures.Count == 0, "Over the p95 target or with errors:\n" + string.Join('\n', failures));
    }

    public static string LastBody = string.Empty;

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string url, Caller caller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(TestAuthHandler.UserHeader, caller.UserId.ToString());
        request.Headers.Add(ContextHeader.Name, caller.AssignmentId.ToString());
        using var response = await client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            LastBody = url + " -> " + (await response.Content.ReadAsStringAsync());
        }

        return response.StatusCode;
    }

    private static async Task<(Dictionary<string, Caller> Callers, World World)> LoadWorldAsync(string connection)
    {
        await using var db = LmsDbContextFactory.Create(connection, TestCurrentContext.PlatformAdmin());
        async Task<Caller> CallerAsync(string name)
        {
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == PerfSeeder.Email(name).ToUpperInvariant());
            var assignment = await db.RoleAssignments.OrderBy(a => a.Id).FirstAsync(a => a.UserId == user.Id);
            return new Caller(user.Id, assignment.Id);
        }

        var callers = new Dictionary<string, Caller>
        {
            ["admin"] = await CallerAsync("perf-admin"),
            ["teacher"] = await CallerAsync("perf-teacher"),
            ["student"] = await CallerAsync("perf-student"),
            ["guardian"] = await CallerAsync("perf-guardian"),
        };
        var cls = await db.Set<Class>().OrderBy(c => c.CodeKey).FirstAsync(c => c.OrganizationId == PerfSeeder.OrganizationId);
        var classCourse = await db.Set<ClassCourse>().OrderBy(c => c.Id).FirstAsync(c => c.ClassId == cls.Id);
        var term = await db.Set<Gradebook>().Where(g => g.ClassCourseId == classCourse.Id).Select(g => g.TermId).FirstAsync();
        return (callers, new World(cls.Id, classCourse.Id, term, cls.OrgUnitId));
    }
}
