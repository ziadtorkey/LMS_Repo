using Lms.Infrastructure.Imports;

namespace Lms.UnitTests.Imports;

public sealed class ImportChecksTests
{
    private static ImportRowValues Row(int number, string? email) =>
        new(number, new Dictionary<string, string?> { ["email"] = email });

    [Fact]
    public void Duplicates_in_file_point_to_the_other_row()
    {
        var rows = new[] { Row(2, "A@x.com"), Row(3, "b@x.com"), Row(4, " a@x.com "), Row(5, null), Row(6, null), Row(7, "B@X.COM") };

        var errors = ImportChecks.DuplicatesInFile(rows, "email", v => v?.Trim().ToLowerInvariant()).ToList();

        Assert.Equal([4, 7], errors.Select(e => e.RowNumber));
        Assert.Equal("2", errors[0].Args!["otherRow"]);
        Assert.Equal("3", errors[1].Args!["otherRow"]);
        Assert.All(errors, e => Assert.Equal("duplicate_in_file", e.Code));
    }

    [Fact]
    public void Date_check_accepts_empty_and_iso_only()
    {
        Assert.Null(ImportChecks.Date(Row(2, null), "email"));
        Assert.Null(ImportChecks.Date(Row(2, "2026-09-01"), "email"));
        Assert.Equal("invalid_date", ImportChecks.Date(Row(2, "1 Sept"), "email")!.Code);
    }
}
