namespace Lms.UnitTests.Architecture;

/// <summary>
/// M5 L6: the student, guardian and staff-preview endpoints read grades only through <c>PublishedGradeReads</c>. They never touch the gradebook
/// tables, the engine loader or the report service, so a new field or a refactor cannot route a hidden item to a student by accident.
/// </summary>
public sealed class PublishedGradeReadsGuardTests
{
    private static readonly string[] StudentFacing =
    [
        "src/Lms.Api/Features/GradeReads/StudentGradeEndpoints.cs",
        "src/Lms.Api/Features/GradeReads/StudentViewPreviewEndpoint.cs",
    ];

    private static readonly string[] Forbidden =
    [
        "GradebookCalculation", "GradeReportService", "Set<GradeItem>", "Set<GradeEntry>", "Set<GradeEvent>", "Set<GradeCategory>",
        "Set<Gradebook>", "IGradebookWriter", "GradebookBackfill",
    ];

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }

    [Fact]
    public void Student_and_guardian_endpoints_use_only_the_published_helper()
    {
        foreach (var relative in StudentFacing)
        {
            var code = string.Join('\n', File.ReadAllLines(Path.Combine(Root(), relative)).Where(l => !l.TrimStart().StartsWith("//")));
            Assert.Contains("PublishedGradeReads", code);
            var offenders = Forbidden.Where(f => code.Contains(f, StringComparison.Ordinal)).ToList();
            Assert.True(offenders.Count == 0, $"{relative} must read grades through PublishedGradeReads only, but uses: {string.Join(", ", offenders)}");
        }
    }

    [Fact]
    public void The_published_helper_always_asks_the_engine_for_published_items_only()
    {
        var code = File.ReadAllText(Path.Combine(Root(), "src/Lms.Infrastructure/Grading/Reads/PublishedGradeReads.cs"));

        Assert.Contains("publishedOnly: true", code);
        Assert.DoesNotContain("publishedOnly: false", code);
    }
}
