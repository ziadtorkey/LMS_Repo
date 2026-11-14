using System.Net;
using Lms.Domain.Common;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Isolation of the M4b tables and sites (M4b plan Task 20, Review Focus 1 and 3, PRD §4.2.3, X-05; CLAUDE.md rule 1). The plan's "Isolation bypass and system-context
/// sites" table is the oracle:
/// <list type="bullet">
/// <item>every entity of the M4b domain namespaces is tenant-owned, carries <c>OrganizationId</c>, and sits behind a global query filter, except the one platform
/// table <c>ZoomWebhookReceipt</c> (which must hold no client data);</item>
/// <item>the files of M4b code that call <c>IgnoreQueryFilters</c> are exactly the reviewed sites, each with its <c>// ISOLATION-BYPASS:</c> reason;</item>
/// <item>the four cross-client re-runs of the table (webhook, package token, validation job, revocation) wait for their code through <see cref="M4bPending"/> and
/// fail with instructions the moment it lands;</item>
/// <item><c>Teacher_cannot_enable_cloud_recording</c> (Review item 9) runs now.</item>
/// </list>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4bIsolationTests(LmsApiFactory factory, ITestOutputHelper output)
{
    private static readonly string[] Namespaces = ["Lms.Domain.LiveSessions", "Lms.Domain.Packages"];

    /// <summary>The platform table: not tenant data, allow-listed, retained 30 days.</summary>
    private const string PlatformTable = "ZoomWebhookReceipt";

    /// <summary>
    /// The files of M4b code that may call <c>IgnoreQueryFilters</c>, with the site of the plan's table (or the reviewed PR) they belong to. A file outside this list that
    /// bypasses the filters fails here and in <c>IsolationBypassGuardTests</c>; add it, with the reason, in the PR that needs it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Bypasses = new Dictionary<string, string>
    {
        ["src/Lms.Infrastructure/Zoom/EfHostSlotStore.cs"] = "site 1: the host pool is platform-wide, slot columns only (Task 5)",
        ["src/Lms.Infrastructure/LiveSessions/LiveSessionService.cs"] = "the daily create cap counts the client's own rows across units, count only (Task 4, #471)",
        ["src/Lms.Infrastructure/Zoom/ZoomEventProcessor.cs"] = "site 2: meeting id to (organization, session) only, then work in that organization's system context (Task 7)",
        ["src/Lms.Infrastructure/Zoom/ZoomPollingJob.cs"] = "site 4: the organizations with live sessions, then each in its own context (Tasks 5, 7)",
        ["src/Lms.Infrastructure/Zoom/ZoomSyncSweepJob.cs"] = "site 4: the organizations with live sessions, then each in its own context (Tasks 5, 7)",
    };

    private static readonly string[] SourceFolders =
    [
        "src/Lms.Infrastructure/LiveSessions", "src/Lms.Infrastructure/Zoom", "src/Lms.Infrastructure/Packages",
        "src/Lms.Api/Features/LiveSessions", "src/Lms.Api/Features/Packages",
    ];

    [Fact]
    public void Every_M4b_entity_is_tenant_owned_unit_scoped_and_filtered_except_the_platform_receipt()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var m4b = db.Model.GetEntityTypes().Where(e => e.ClrType.Namespace is { } ns && Namespaces.Any(n => ns.StartsWith(n, StringComparison.Ordinal))).ToList();
        output.WriteLine("M4b entities: " + string.Join(", ", m4b.Select(e => e.ClrType.Name)));
        Assert.NotEmpty(m4b);

        var tenant = m4b.Where(e => e.ClrType.Name != PlatformTable).ToList();
        var notTenant = tenant.Where(e => !typeof(ITenantOwned).IsAssignableFrom(e.ClrType)).Select(e => e.ClrType.Name).ToList();
        var noOrg = tenant.Where(e => e.FindProperty("OrganizationId") is null).Select(e => e.ClrType.Name).ToList();
        var noUnit = tenant.Where(e => e.FindProperty("OrgUnitId") is null || e.FindProperty("OrgUnitPath") is null).Select(e => e.ClrType.Name).ToList();
        var noFilter = tenant.Where(e => !e.GetDeclaredQueryFilters().Any()).Select(e => e.ClrType.Name).ToList();
        Assert.True(notTenant.Count == 0, "M4b entities that are not ITenantOwned (CLAUDE.md rule 1): " + string.Join(", ", notTenant));
        Assert.True(noOrg.Count == 0, "M4b entities without OrganizationId: " + string.Join(", ", noOrg));
        Assert.True(noUnit.Count == 0, "M4b entities without OrgUnitId and OrgUnitPath (unit-scoped, plan 'Isolation shapes'): " + string.Join(", ", noUnit));
        Assert.True(noFilter.Count == 0, "M4b entities without a global query filter: " + string.Join(", ", noFilter));
    }

    [Fact]
    public void The_platform_receipt_holds_no_client_data()
    {
        if (M4bPending.Skip(factory, output, M4bPending.ZoomEventProcessor))
        {
            return;
        }

        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var receipt = db.Model.GetEntityTypes().Single(e => e.ClrType.Name == PlatformTable);
        Assert.False(typeof(ITenantOwned).IsAssignableFrom(receipt.ClrType), "the receipt is a platform table, not tenant data");
        var properties = receipt.GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(properties, p => p is "OrganizationId" or "OrgUnitId" or "OrgUnitPath" or "UserId" or "SessionId" or "MeetingId" or "Payload" or "Body");
    }

    [Fact]
    public void Only_the_reviewed_M4b_files_bypass_the_query_filters_and_each_says_why()
    {
        var root = RepoRoot();
        var found = new List<string>();
        foreach (var folder in SourceFolders)
        {
            var path = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains("IgnoreQueryFilters", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    found.Add(relative);
                    var window = string.Join('\n', lines.Skip(Math.Max(0, i - 3)).Take(4));
                    Assert.True(window.Contains("ISOLATION-BYPASS", StringComparison.Ordinal), $"{relative}:{i + 1} bypasses the query filters without an '// ISOLATION-BYPASS: reason' comment.");
                }
            }
        }

        var unlisted = found.Distinct().Where(f => !Bypasses.ContainsKey(f)).ToList();
        Assert.True(unlisted.Count == 0, "M4b files bypassing the query filters without a reviewed entry in M4bIsolationTests.Bypasses (and IsolationBypassGuardTests): " + string.Join(", ", unlisted));
    }

    // ---- the cross-client re-runs of the plan table ----

    /// <summary>
    /// The cross-client re-runs of the plan's table that the backend wrote with its code (package host, validation job, revocation): the oracle names them, so they must exist
    /// as tests; their strength is checked by the mutation record (<c>docs/security/m4b-probe-mutations.md</c>).
    /// </summary>
    [Theory]
    [InlineData("Token_only_reaches_its_own_organization", "PackageHostTests")]
    [InlineData("Revocation_of_client_a_does_not_affect_client_b", "PackageHostTests")]
    [InlineData("Validation_job_cannot_read_another_clients_upload", "PackageValidationJobTests")]
    public void The_named_cross_client_tests_of_the_plan_exist(string method, string type)
    {
        var found = typeof(M4bIsolationTests).Assembly.GetTypes().Where(t => t.Name == type)
            .SelectMany(t => t.GetMethods()).Any(m => m.Name == method && m.GetCustomAttributes(typeof(FactAttribute), false).Length > 0);
        Assert.True(found, $"{type}.{method} is named by the M4b plan (isolation sites table) and must exist as a test.");
    }

    [Fact]
    public void Webhook_for_client_a_meeting_cannot_touch_client_b_rows()
    {
        if (M4bPending.Skip(factory, output, M4bPending.ZoomEventProcessor))
        {
            return;
        }

        Assert.Fail("#511 is on this branch: write this probe. Two clients each with a live session with an ExternalMeetingId; send a correctly signed meeting.ended for client A's meeting id " +
                    "(see M4bWebhookProbeTests.Request) and assert client A's session ended and client B's session, its participants and its recording state are unchanged; then a signed event whose " +
                    "meeting id is client B's but whose payload names client A's account touches only client B's row (the meeting id decides, never the payload).");
    }

    // ---- Review item 9: the setting that turns on cloud recording ----

    [Fact]
    public async Task Teacher_cannot_enable_cloud_recording()
    {
        using var world = await M4bWorld.CreateAsync(factory, "M4CR");
        var tree = world.Content.Tree;
        var url = Routes.OrgUnitSetting(tree.Root.Id, SettingKeys.LiveCloudRecordingEnabled.Name);
        var body = new SetSettingRequest("true", false);

        foreach (var caller in new[] { M4bCaller.Teacher, M4bCaller.OtherTeacher, M4bCaller.StudentGroup1, M4bCaller.Guardian })
        {
            using var response = await world.SendAsync(caller, HttpMethod.Put, url, body);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{caller} setting Live.CloudRecordingEnabled → {(int)response.StatusCode}, expected 403 (Settings.Update).");
        }

        using var other = await world.SendAsync(M4bCaller.ClientBAdmin, HttpMethod.Put, url, body);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode); // another client's unit is invisible

        using var admin = await world.SendAsync(M4bCaller.Admin, HttpMethod.Put, url, body);
        Assert.True(admin.IsSuccessStatusCode, $"the client's Org Admin must be able to set it, got {(int)admin.StatusCode}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("LMS.slnx not found above " + AppContext.BaseDirectory);
    }
}
