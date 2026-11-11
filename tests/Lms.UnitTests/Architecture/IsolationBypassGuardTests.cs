namespace Lms.UnitTests.Architecture;

/// <summary>
/// CLAUDE.md rule 1 / PRD §4.2: global query filters (client, subtree, soft delete) may be bypassed only in reviewed
/// places. Every <c>IgnoreQueryFilters()</c> statement in <c>src</c> must carry a <c>// ISOLATION-BYPASS: reason</c>
/// comment within the 3 lines above it, and its file must be on the allow-list below (M1 plan, Global Constraints).
/// A PR that adds a new bypass adds its file here with the reason, so the allow-list change is reviewed by QA.
/// </summary>
public sealed class IsolationBypassGuardTests
{
    /// <summary>Repository-relative files allowed to bypass the global query filters, with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>
    {
        ["src/Lms.Infrastructure/Organizations/OrgTree.cs"] =
            "client root and ancestors lie above a unit-level caller's scope (M1 Task 5)",
        ["src/Lms.Infrastructure/Access/ValidAssignmentQuery.cs"] =
            "resolving the caller's own role assignments before any scope exists; D15 inactive ancestors (M0 Task 5, #86)",
        ["src/Lms.Infrastructure/Configuration/PolicyEvaluator.cs"] =
            "policies set on ancestors outside the caller's scope (M0 Task 6)",
        ["src/Lms.Infrastructure/Configuration/PolicyManager.cs"] =
            "restore a soft-deleted policy row held by the unique index (#25)",
        ["src/Lms.Infrastructure/Configuration/SettingsResolver.cs"] =
            "settings on ancestors; restore a soft-deleted setting row (M0 Task 6, #25)",
        ["src/Lms.Infrastructure/Configuration/EffectiveSettingsReader.cs"] =
            "effective settings and locks inherited from ancestors above the caller's scope (M1 Task 14, #100)",
        ["src/Lms.Api/Features/LevelTypes/LevelTypeEndpoints.cs"] =
            "level types are client-wide, so the in-use count covers the whole client, not the caller subtree (M1 Task 8, #83)",
        ["src/Lms.Infrastructure/Notifications/NotificationRecipientResolver.cs"] =
            "recipients at or below the event unit, raised by someone above or beside them (M1 Task 10, #90)",
        ["src/Lms.Infrastructure/AcademicYears/AcademicYearResolver.cs"] =
            "effective year list inherited from ancestors above the caller's scope (M1 Task 20, #102)",
        ["src/Lms.Api/Features/AcademicYears/AcademicYearMapper.cs"] =
            "names of the defining unit of an inherited year, restricted to the caller's organization (M1 Task 20, #102)",
        ["src/Lms.Api/Features/Audit/AuditRows.cs"] =
            "role and unit names of audit rows the caller may already see, restricted to the entries' clients (M1 Task 18, #97)",
        ["src/Lms.Api/Features/AcademicYears/AcademicYearEndpoints.cs"] =
            "delete also removes calendar events at descendant units, restricted to the year's client (M1 Task 22, #57)",
        ["src/Lms.Api/Features/AcademicYears/CalendarEndpoints.cs"] =
            "events and names inherited from ancestor units, restricted to the unit's client (M1 Task 22, #57)",
        ["src/Lms.Infrastructure/AcademicYears/AcademicCalendar.cs"] =
            "holidays of ancestor units apply to descendants, restricted to the client and effective years (M1 Task 22, #57)",
    };

    [Fact]
    public void Every_IgnoreQueryFilters_statement_carries_the_ISOLATION_BYPASS_marker()
    {
        var violations = SourceFiles()
            .SelectMany(file => IsolationBypassScanner.FindUnmarkedCalls(File.ReadAllLines(file.FullPath))
                .Select(call => $"{file.RelativePath}:{call.Line}: {call.Text.Trim()}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "IgnoreQueryFilters() without a '// ISOLATION-BYPASS: <reason>' comment within the 3 lines above the statement "
            + "(CLAUDE.md rule 1):\n" + string.Join('\n', violations));
    }

    [Fact]
    public void Every_file_with_IgnoreQueryFilters_is_on_the_allow_list()
    {
        var notAllowed = SourceFiles()
            .Where(file => IsolationBypassScanner.FindCalls(File.ReadAllLines(file.FullPath)).Count > 0)
            .Select(file => file.RelativePath)
            .Where(path => !AllowList.ContainsKey(path))
            .ToList();

        Assert.True(notAllowed.Count == 0,
            "IgnoreQueryFilters() in files that are not on the allow-list in IsolationBypassGuardTests "
            + "(add the file with its reason in the same PR so QA reviews it):\n" + string.Join('\n', notAllowed));
    }

    [Fact]
    public void Allow_list_has_no_stale_entries()
    {
        var withCalls = SourceFiles()
            .Where(file => IsolationBypassScanner.FindCalls(File.ReadAllLines(file.FullPath)).Count > 0)
            .Select(file => file.RelativePath)
            .ToHashSet();

        var stale = AllowList.Keys.Where(path => !withCalls.Contains(path)).ToList();

        Assert.True(stale.Count == 0,
            "Allow-list entries whose file no longer bypasses the filters (remove them):\n" + string.Join('\n', stale));
    }

    [Fact]
    public void The_scan_actually_sees_the_source_tree()
    {
        // Guards against a silently empty scan (wrong root, renamed folder) turning the checks above green.
        Assert.Contains(SourceFiles(), f => f.RelativePath == "src/Lms.Infrastructure/Organizations/OrgTree.cs");
    }

    private sealed record SourceFile(string FullPath, string RelativePath);

    private static List<SourceFile> SourceFiles()
    {
        var root = RepoRoot();
        var src = Path.Combine(root, "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(full => new SourceFile(full, Path.GetRelativePath(root, full).Replace('\\', '/')))
            .Where(f => !f.RelativePath.Contains("/Migrations/", StringComparison.Ordinal)
                        && !f.RelativePath.Contains("/obj/", StringComparison.Ordinal)
                        && !f.RelativePath.Contains("/bin/", StringComparison.Ordinal))
            .ToList();
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (LMS.slnx) not found above " + AppContext.BaseDirectory);
    }
}
