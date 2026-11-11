using ClosedXML.Excel;
using Lms.Infrastructure.Imports;
using Lms.Shared.Imports;

namespace Lms.UnitTests.Imports;

public sealed class ImportReaderTests
{
    private static readonly IReadOnlyList<ImportColumn> Columns =
    [
        new("code", "الرمز", "Code", true),
        new("name", "الاسم", "Full Name", true),
        new("start", "تاريخ البدء", "Start date", false),
    ];

    private static MemoryStream Sheet(string[] headers, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                switch (rows[r][c])
                {
                    case null:
                        break;
                    case DateTime date:
                        sheet.Cell(r + 2, c + 1).Value = date;
                        break;
                    default:
                        sheet.Cell(r + 2, c + 1).Value = rows[r][c]!.ToString();
                        break;
                }
            }
        }

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Headers_match_in_arabic_english_or_key_ignoring_case_and_spaces()
    {
        using var file = Sheet(["  CODE ", "الاسم", "start  DATE"], ["a1", "Ali", "2026-09-01"]);

        var result = await ImportReader.ReadAsync(file, Columns, default);

        Assert.Empty(result.FileErrors);
        var row = Assert.Single(result.Rows);
        Assert.Equal("a1", row.Get("code"));
        Assert.Equal("Ali", row.Get("name"));
        Assert.Equal("2026-09-01", row.Get("start"));
    }

    [Fact]
    public async Task Unknown_and_missing_columns_are_file_errors()
    {
        using var file = Sheet(["Code", "Shoe size"], ["a1", "42"]);

        var result = await ImportReader.ReadAsync(file, Columns, default);

        Assert.Contains(result.FileErrors, e => e.Code == "unknown_column" && e.Args!["column"] == "Shoe size");
        Assert.Contains(result.FileErrors, e => e.Code == "missing_column" && e.Args!["column"] == "Full Name");
        Assert.All(result.FileErrors, e => Assert.Equal(0, e.RowNumber));
    }

    [Fact]
    public async Task Blank_rows_are_skipped_and_row_numbers_are_excel_rows()
    {
        using var file = Sheet(["Code", "Full Name"], ["a1", "Ali"], [null, null], ["a3", "Sara"]);

        var result = await ImportReader.ReadAsync(file, Columns, default);

        Assert.Equal([2, 4], result.Rows.Select(r => r.RowNumber));
        Assert.Equal("Sara", result.Rows[1].Get("name"));
    }

    [Fact]
    public async Task Excel_date_and_iso_text_both_parse()
    {
        using var file = Sheet(["Code", "Full Name", "Start date"],
            ["a1", "Ali", new DateTime(2026, 9, 1)], ["a2", "Sara", "2026-10-05"], ["a3", "Omar", "01/09/2026"]);

        var result = await ImportReader.ReadAsync(file, Columns, default);

        Assert.Equal("2026-09-01", result.Rows[0].Get("start"));
        Assert.Equal("2026-10-05", result.Rows[1].Get("start"));
        Assert.True(ImportChecks.TryParseDate(result.Rows[0].Get("start"), out _));
        Assert.False(ImportChecks.TryParseDate(result.Rows[2].Get("start"), out _));
    }

    [Fact]
    public async Task Empty_file_and_non_excel_content_are_file_errors()
    {
        using var empty = Sheet(["Code", "Full Name"]);
        using var garbage = new MemoryStream("not an excel file"u8.ToArray());

        Assert.Equal("empty_file", Assert.Single((await ImportReader.ReadAsync(empty, Columns, default)).FileErrors).Code);
        Assert.Equal("unreadable_file", Assert.Single((await ImportReader.ReadAsync(garbage, Columns, default)).FileErrors).Code);
    }

    [Fact]
    public async Task More_than_the_row_limit_is_one_too_many_rows_error()
    {
        var rows = Enumerable.Range(0, ImportLimits.MaxRows + 1).Select(i => new object?[] { "c" + i, "n" }).ToArray();
        using var file = Sheet(["Code", "Full Name"], rows);

        var result = await ImportReader.ReadAsync(file, Columns, default);

        Assert.Equal("too_many_rows", Assert.Single(result.FileErrors).Code);
        Assert.Empty(result.Rows);
    }
}
