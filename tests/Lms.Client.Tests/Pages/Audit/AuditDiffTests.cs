using Lms.Client.Pages.Audit;

namespace Lms.Client.Tests.Pages.Audit;

public sealed class AuditDiffTests
{
    [Fact]
    public void Diff_lists_changed_properties_only_for_updates()
    {
        var rows = AuditDiff.Rows("""{"Name":"A","Code":"X","Active":true}""", """{"Name":"B","Code":"X","Active":false}""");

        Assert.Equal(["Name", "Active"], rows.Select(r => r.Property));
        Assert.Equal(new AuditDiffRow("Name", "A", "B"), rows[0]);
        Assert.Equal(new AuditDiffRow("Active", "true", "false"), rows[1]);
    }

    [Fact]
    public void Diff_of_create_shows_after_only()
    {
        var rows = AuditDiff.Rows(null, """{"Name":"B","Code":"X"}""");

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Before));
        Assert.Equal("B", rows[0].After);
    }

    [Fact]
    public void Diff_of_delete_shows_before_only()
    {
        var rows = AuditDiff.Rows("""{"Name":"A"}""", null);

        Assert.Equal(new AuditDiffRow("Name", "A", null), Assert.Single(rows));
    }

    [Fact]
    public void Arabic_values_and_invalid_json_are_handled()
    {
        var rows = AuditDiff.Rows("""{"NameAr":"المعلم"}""", """{"NameAr":"المدرس"}""");

        Assert.Equal(new AuditDiffRow("NameAr", "المعلم", "المدرس"), Assert.Single(rows));
        Assert.Empty(AuditDiff.Rows("not json", "also not"));
    }
}
