using System.Text.RegularExpressions;

namespace Lms.UnitTests.Architecture;

/// <summary>
/// M2 plan, Global Constraints (people isolation shapes): <c>GuardianProfile</c> is tenant-owned only, so the table
/// filter is client-wide. A guardian is visible to a caller only through a live <c>StudentGuardian</c> row in the
/// caller's scope, so every guardian query starts from <c>StudentGuardian</c>. This guard makes the convention a CI rule:
/// code that reads the guardian table directly must be on the allow-list below, with the reason, so QA reviews each one.
/// An M3 endpoint that lists or opens guardians reads <c>StudentGuardians</c> (unit-scoped) and joins guardians from
/// there; it needs no entry.
/// </summary>
public sealed partial class GuardianAccessGuardTests
{
    /// <summary>Repository-relative files that may read the guardian table directly, with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowList = new Dictionary<string, string>
    {
        ["src/Lms.Infrastructure/People/PersonDuplicateFinder.cs"] =
            "client-wide match by national id or mobile (guardians may already exist through a sibling subtree); returns the entity to "
            + "Infrastructure callers only, the M3 match endpoint projects name and masked mobile (M2 Task 5, #157)",
        ["src/Lms.Infrastructure/Messaging/ConversationService.cs"] =
            "existence check only: whether a user id has a guardian profile in the caller client (client filter of the table), to refuse a conversation with a stranger; no row leaves (M6 Task 10)",
        ["src/Lms.Infrastructure/Messaging/GroupPostingRules.cs"] =
            "existence check only: whether the poster user id has a guardian profile in the caller client, to apply the D77 group posting rule; no row leaves (M6 Task 10)",
        ["src/Lms.Api/Features/Guardians/GuardianPortalEndpoints.cs"] =
            "my children: the signed-in user's own guardian profile (by their user id, client filter of the table); children come from StudentGuardian in the caller's scope (M3 Task 25, #233)",
        ["src/Lms.Infrastructure/Enrollment/TransferNotifier.cs"] =
            "portal user ids of the guardians linked to the transferred student (links in the caller's scope); only user ids leave (M3 Task 28, #236)",
        ["src/Lms.Infrastructure/People/PersonStatusService.cs"] =
            "recompute a user's account status: only whether the user has a live guardian profile (M2 Task 6, #158)",
        ["src/Lms.Infrastructure/Identity/External/ExternalLoginLinker.cs"] =
            "external sign-in matches a person by national id or user id among the provider's own client only; returns user ids, "
            + "never guardian data (M2 Task 22, #168 / PR #193)",
        ["src/Lms.Infrastructure/People/Guardians/GuardianVisibility.cs"] =
            "the one entry point for reading guardians: only guardians with a live StudentGuardian in the caller's scope (M3 Task 19, #227)",
        ["src/Lms.Infrastructure/People/Guardians/GuardianLinker.cs"] =
            "creates, links and edits guardians of the caller's own client (tenant filter); existing ids are client-checked, "
            + "duplicate answers carry the field name only (M3 Task 19, #227)",
        ["src/Lms.Infrastructure/Persistence/Configurations/People/PeopleConfigurations.cs"] =
            "entity mapping",
    };

    /// <summary>Direct access to the table: the DbSet name, <c>Set&lt;GuardianProfile&gt;()</c> or an <c>IEntityTypeConfiguration</c>.</summary>
    [GeneratedRegex(@"GuardianProfiles\b|Set<[\w.\s]*\bGuardianProfile\s*>|EntityTypeBuilder<[\w.\s]*\bGuardianProfile\s*>")]
    private static partial Regex TableAccess();

    [Fact]
    public void Only_allow_listed_files_read_the_guardian_table_directly()
    {
        var offenders = Sources().Where(f => TableAccess().IsMatch(File.ReadAllText(f.FullPath)))
            .Select(f => f.RelativePath)
            .Where(path => !AllowList.ContainsKey(path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files read GuardianProfile directly. A guardian is visible only through a live StudentGuardian row in the caller's "
            + "scope: start the query from StudentGuardians, or add the file to GuardianAccessGuardTests.AllowList with the reason "
            + "(QA reviews every entry):\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void Allow_list_has_no_stale_entries()
    {
        var reading = Sources().Where(f => TableAccess().IsMatch(File.ReadAllText(f.FullPath))).Select(f => f.RelativePath).ToHashSet();

        var stale = AllowList.Keys.Where(path => !reading.Contains(path)).ToList();

        Assert.True(stale.Count == 0,
            "Allow-list entries whose file no longer reads the guardian table (remove them):\n" + string.Join('\n', stale));
    }

    [Fact]
    public void The_scan_actually_sees_the_source_tree()
    {
        // Guards against a silently empty scan (wrong root, renamed folder) turning the checks above green.
        Assert.Contains(Sources(), f => f.RelativePath == "src/Lms.Infrastructure/People/PersonDuplicateFinder.cs");
    }

    [Theory]
    [InlineData("db.Set<GuardianProfile>().Where(g => true)", true)]
    [InlineData("db.GuardianProfiles.Where(g => true)", true)]
    [InlineData("db.Set< GuardianProfile >()", true)]
    [InlineData("db.Set<Lms.Domain.People.GuardianProfile>()", true)]
    [InlineData("db.Set<StudentGuardian>().Select(l => l.GuardianId)", false)]
    [InlineData("GuardianProfile guardian = link.Guardian;", false)]
    public void The_pattern_flags_table_access_only(string code, bool flagged) =>
        Assert.Equal(flagged, TableAccess().IsMatch(code));

    private sealed record SourceFile(string FullPath, string RelativePath);

    private static List<SourceFile> Sources()
    {
        var root = RepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(full => new SourceFile(full, Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/')))
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
