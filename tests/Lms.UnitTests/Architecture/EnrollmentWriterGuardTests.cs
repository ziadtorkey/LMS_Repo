using System.Text.RegularExpressions;

namespace Lms.UnitTests.Architecture;

/// <summary>
/// M3 plan, Global Constraints "One enrollment writer" (D9): only the enrollment engine creates, changes or removes
/// <c>CourseEnrollment</c> and <c>ClassStudent</c> rows, so every path checks the class, the academic year and the course
/// restrictions. The engine lives in <c>src/Lms.Infrastructure/Enrollment</c>; anything else that builds or writes these
/// rows fails this test. Reading them is fine.
/// </summary>
public sealed partial class EnrollmentWriterGuardTests
{
    private const string EngineFolder = "src/Lms.Infrastructure/Enrollment/";

    /// <summary>Construction, set writes and the mutators of the two entities.</summary>
    [GeneratedRegex(
        @"new\s+(?:[\w.]+\.)?(?:CourseEnrollment|ClassStudent)\s*\("
        + @"|Set<[\w.\s]*\b(?:CourseEnrollment|ClassStudent)\s*>\s*\(\s*\)\s*\.\s*(?:Add|AddRange|AddAsync|AddRangeAsync|Remove|RemoveRange|Update|UpdateRange|Attach)\b"
        + @"|\b(?:CourseEnrollments|ClassStudents)\s*\.\s*(?:Add|AddRange|AddAsync|Remove|RemoveRange|Update)\b"
        + @"|ExecuteSql\w*\s*\([^;]*\b(?:INSERT|UPDATE|DELETE)\b[^;]*`?(?:CourseEnrollments|ClassStudents)\b")]
    private static partial Regex Write();

    [Fact]
    public void Only_the_enrollment_engine_writes_enrollments_and_placements()
    {
        var offenders = Sources()
            .Where(f => !f.RelativePath.StartsWith(EngineFolder, StringComparison.Ordinal))
            .Where(f => !f.RelativePath.StartsWith("src/Lms.Domain/", StringComparison.Ordinal)) // the entities' own constructors
            // Development-only demo data (never runs outside Development): it places the sample students of the M6 messaging demo in a sample class on
            // purpose, without the engine's restriction checks, because the seeder has no request context.
            .Where(f => f.RelativePath != "src/Lms.Infrastructure/Seeding/M6DemoSeeder.cs")
            .Where(f => Write().IsMatch(File.ReadAllText(f.FullPath)))
            .Select(f => f.RelativePath)
            .ToList();

        Assert.True(offenders.Count == 0,
            "CourseEnrollment and ClassStudent are written only by IEnrollmentEngine (src/Lms.Infrastructure/Enrollment). "
            + "Call the engine instead of writing them here:\n" + string.Join('\n', offenders));
    }

    [Fact]
    public void The_scan_actually_sees_the_engine()
    {
        var engine = Sources().Single(f => f.RelativePath == EngineFolder + "EnrollmentEngine.cs");

        Assert.Matches(Write(), File.ReadAllText(engine.FullPath)); // the pattern really recognizes the engine's own writes
    }

    [Theory]
    [InlineData("var e = new CourseEnrollment(cc, placement, \"Auto\", on);", true)]
    [InlineData("db.Add(new ClassStudent(cls, id, on));", true)]
    [InlineData("var p = new Lms.Domain.Classes.ClassStudent(cls, id, on);", true)]
    [InlineData("db.Set<CourseEnrollment>().Add(e);", true)]
    [InlineData("db.Set<ClassStudent>().AddRange(rows);", true)]
    [InlineData("db.Set<Lms.Domain.Enrollment.CourseEnrollment>().Remove(e);", true)]
    [InlineData("db.CourseEnrollments.Add(e);", true)]
    [InlineData("db.Set<CourseEnrollment>().Where(e => e.StudentId == id)", false)]
    [InlineData("db.Set<ClassStudent>().CountAsync(c => c.ClassId == id)", false)]
    [InlineData("var rows = await db.Set<CourseEnrollment>().ToListAsync();", false)]
    public void The_pattern_flags_writes_only(string code, bool flagged) => Assert.Equal(flagged, Write().IsMatch(code));

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
