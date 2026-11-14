using Lms.Domain.Common;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Isolation of the M6 tables (M6 plan Task 31, CLAUDE.md rule 1, PRD §4.2.3): every entity of the M6 domain namespaces is tenant-owned and sits behind a
/// global query filter, and the places that bypass the filters in M6 code are exactly the reviewed ones. Both checks read the model and the source, so a
/// table added by any M6 PR (messages, conversations, participants, reports, blocks, announcements, recipients, attachments, outbox, push subscriptions, ...)
/// is covered the moment it is merged, without editing this file. Row-level cross-client tests per table live next to each feature (the plan's sites table);
/// this class is the guard that a table cannot be added without the filter.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M6IsolationTests(LmsApiFactory factory, ITestOutputHelper output)
{
    /// <summary>The domain namespaces that belong to M6.</summary>
    private static readonly string[] Namespaces =
        ["Lms.Domain.Messaging", "Lms.Domain.Announcements", "Lms.Domain.Comms", "Lms.Domain.Moderation"];

    /// <summary>
    /// Repository-relative files of M6 code that may call <c>IgnoreQueryFilters()</c>, with the reason. Pinned here (and in the
    /// <c>IsolationBypassGuardTests</c> allow-list) so a new M6 bypass is a reviewed change. Empty until a site of the plan's table lands.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> M6Bypasses = new Dictionary<string, string>();

    private static readonly string[] M6SourceFolders =
    [
        "src/Lms.Api/Features/Messaging", "src/Lms.Api/Features/Announcements", "src/Lms.Api/Features/Comms", "src/Lms.Api/Features/Moderation",
        "src/Lms.Infrastructure/Messaging", "src/Lms.Infrastructure/Announcements", "src/Lms.Infrastructure/Comms", "src/Lms.Infrastructure/Moderation",
    ];

    [Fact]
    public void Every_M6_entity_is_tenant_owned_and_has_a_query_filter()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var m6 = db.Model.GetEntityTypes().Where(e => e.ClrType.Namespace is { } ns && Namespaces.Any(n => ns.StartsWith(n, StringComparison.Ordinal))).ToList();
        output.WriteLine(m6.Count == 0 ? "No M6 entity on this branch yet." : "M6 entities: " + string.Join(", ", m6.Select(e => e.ClrType.Name)));

        var notTenant = m6.Where(e => !typeof(ITenantOwned).IsAssignableFrom(e.ClrType)).Select(e => e.ClrType.Name).ToList();
        var noFilter = m6.Where(e => !e.GetDeclaredQueryFilters().Any()).Select(e => e.ClrType.Name).ToList();
        Assert.True(notTenant.Count == 0, "M6 entities that are not ITenantOwned (CLAUDE.md rule 1): " + string.Join(", ", notTenant));
        Assert.True(noFilter.Count == 0, "M6 entities without a global query filter: " + string.Join(", ", noFilter));
    }

    [Fact]
    public void Every_M6_entity_carries_the_organization_column()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var missing = db.Model.GetEntityTypes()
            .Where(e => e.ClrType.Namespace is { } ns && Namespaces.Any(n => ns.StartsWith(n, StringComparison.Ordinal)))
            .Where(e => e.FindProperty("OrganizationId") is null)
            .Select(e => e.ClrType.Name).ToList();
        Assert.True(missing.Count == 0, "M6 entities without OrganizationId: " + string.Join(", ", missing));
    }

    [Fact]
    public void Only_the_reviewed_M6_files_bypass_the_query_filters()
    {
        var root = RepoRoot();
        var found = new List<string>();
        foreach (var folder in M6SourceFolders)
        {
            var path = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (File.ReadAllText(file).Contains("IgnoreQueryFilters", StringComparison.Ordinal))
                {
                    found.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                }
            }
        }

        var unlisted = found.Where(f => !M6Bypasses.ContainsKey(f)).ToList();
        var stale = M6Bypasses.Keys.Where(k => !found.Contains(k)).ToList();
        Assert.True(unlisted.Count == 0, "M6 files bypassing the query filters without a reviewed entry in M6IsolationTests.M6Bypasses (and IsolationBypassGuardTests): " + string.Join(", ", unlisted));
        Assert.True(stale.Count == 0, "Entries of M6IsolationTests.M6Bypasses that no longer bypass: " + string.Join(", ", stale));
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
