namespace Lms.UnitTests.Architecture;

/// <summary>Self-tests of the textual scanner behind <see cref="IsolationBypassGuardTests"/>.</summary>
public sealed class IsolationBypassScannerTests
{
    private static IReadOnlyList<int> Unmarked(string source) =>
        IsolationBypassScanner.FindUnmarkedCalls(source.Split('\n')).Select(c => c.Line).ToList();

    [Fact]
    public void Call_with_marker_directly_above_is_accepted()
    {
        const string source = """
            // ISOLATION-BYPASS: ancestors lie above the caller's scope.
            var rows = await db.Items.IgnoreQueryFilters().ToListAsync();
            """;

        Assert.Empty(Unmarked(source));
    }

    [Fact]
    public void Call_without_any_marker_is_reported_with_its_line()
    {
        const string source = """
            var a = 1;
            var rows = await db.Items.IgnoreQueryFilters().ToListAsync();
            """;

        Assert.Equal([2], Unmarked(source));
    }

    [Fact]
    public void A_plain_justification_comment_is_not_enough()
    {
        const string source = """
            // IgnoreQueryFilters: ancestors lie above the caller's scope.
            var rows = await db.Items.IgnoreQueryFilters().ToListAsync();
            """;

        Assert.Equal([2], Unmarked(source));
    }

    [Fact]
    public void Multi_line_statement_is_covered_by_the_marker_above_its_first_line()
    {
        const string source = """
            var x = 0;
            // ISOLATION-BYPASS: resolving the caller's own contexts.
            var query =
                from a in db.A.IgnoreQueryFilters()
                join b in db.B.IgnoreQueryFilters() on a.Id equals b.Id
                join c in db.C.IgnoreQueryFilters() on a.Id equals c.Id
                select a;
            """;

        Assert.Empty(Unmarked(source));
    }

    [Fact]
    public void A_second_statement_needs_its_own_marker()
    {
        const string source = """
            // ISOLATION-BYPASS: names of rows the caller can already see.
            var roles = await db.Roles.IgnoreQueryFilters().ToListAsync();
            var units = await db.Units.IgnoreQueryFilters().ToListAsync();
            """;

        Assert.Equal([3], Unmarked(source));
    }

    [Fact]
    public void Marker_more_than_three_lines_above_the_statement_does_not_count()
    {
        const string source = """
            // ISOLATION-BYPASS: too far away.
            var a = 1;
            var b = 2;
            var c = 3;
            var rows = await db.Items.IgnoreQueryFilters().ToListAsync();
            """;

        Assert.Equal([5], Unmarked(source));
    }

    [Fact]
    public void Mentions_in_comments_are_not_calls()
    {
        const string source = """
            // We never call IgnoreQueryFilters() here.
            /// <summary>IgnoreQueryFilters() is forbidden.</summary>
             * IgnoreQueryFilters() in a block comment
            var rows = await db.Items.ToListAsync();
            """;

        Assert.Empty(Unmarked(source));
    }
}
