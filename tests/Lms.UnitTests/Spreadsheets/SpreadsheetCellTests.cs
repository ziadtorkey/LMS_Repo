using System.IO.Compression;
using ClosedXML.Excel;
using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Exchange;
using Lms.Infrastructure.Spreadsheets;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.UnitTests.Spreadsheets;

public sealed class SpreadsheetCellTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "'=HYPERLINK(\"http://x\")")]
    [InlineData("+1+1", "'+1+1")]
    [InlineData("-2", "'-2")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("\rcmd", "'\rcmd")]
    [InlineData("plain", "plain")]
    [InlineData("a=b", "a=b")]
    [InlineData("", "")]
    public void Export_cells_escape_formula_injection(string input, string expected) => Assert.Equal(expected, SpreadsheetCell.Safe(input));

    [Fact]
    public void A_null_value_is_an_empty_cell() => Assert.Equal(string.Empty, SpreadsheetCell.Safe(null));

    [Fact]
    public void The_column_export_escapes_every_text_cell_and_keeps_points_numeric()
    {
        var bytes = GradeColumnExporter.Export("=Evil title", 20, [new ColumnExportRow("=S1", "+Name", "@اسم", 12.5m)]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        // ClosedXML stores a leading apostrophe as a quote prefix: the cell is text that Excel never evaluates
        foreach (var column in new[] { 1, 2, 3, 6 })
        {
            Assert.Equal(ClosedXML.Excel.XLDataType.Text, sheet.Cell(2, column).DataType);
            Assert.False(sheet.Cell(2, column).HasFormula);
            Assert.True(sheet.Cell(2, column).Style.IncludeQuotePrefix);
        }

        Assert.Equal("=S1", sheet.Cell(2, 1).GetString());
        Assert.Equal("+Name", sheet.Cell(2, 2).GetString());
        Assert.Equal(12.5, sheet.Cell(2, 4).GetDouble());
    }

    [Fact]
    public void The_report_excel_has_the_scale_sheet_with_the_bands_and_escapes_text()
    {
        var unit = Lms.Domain.Organizations.OrgUnit.CreateRoot(Guid.NewGuid(), Guid.NewGuid(), "ج", "Root", "R");
        var scale = GradingScale.CreatePlain(unit, "سلم", "=Scale", "Letter", isDefault: false);
        var band = new GradingScaleBand(scale, 0, 0, 100, "A", "ممتاز", "Excellent", "وصف", "Outstanding", null);
        var report = new GradeReportDto(
            GradeReportKinds.Class,
            new PagedResult<GradeReportRowDto>([new GradeReportRowDto(Guid.NewGuid(), "ع", "=Student", "S1", Guid.NewGuid(), "م", "Math", 91.5m, "A", "ممتاز", "Excellent", null)], 1, 1, 50),
            DateTime.UtcNow, DateTime.UtcNow);

        var bytes = GradeReportExcel.Render(report, scale, [band]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Report", "Scale"], workbook.Worksheets.Select(w => w.Name));
        Assert.False(workbook.Worksheet("Report").Cell(2, 2).HasFormula);
        Assert.True(workbook.Worksheet("Report").Cell(2, 2).Style.IncludeQuotePrefix);
        Assert.Equal("=Student", workbook.Worksheet("Report").Cell(2, 2).GetString());
        Assert.Equal(91.5, workbook.Worksheet("Report").Cell(2, 6).GetDouble());
        var legend = workbook.Worksheet("Scale");
        Assert.True(legend.Cell(2, 1).Style.IncludeQuotePrefix);
        Assert.Equal(("=Scale", "A", "Excellent", "Outstanding"), (legend.Cell(2, 1).GetString(), legend.Cell(2, 3).GetString(), legend.Cell(2, 6).GetString(), legend.Cell(2, 8).GetString()));
    }

    // --- the importer (L9)

    private static byte[] Workbook(Action<IXLWorksheet> fill)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Grades");
        sheet.Cell(1, 1).Value = "StudentNumber";
        sheet.Cell(1, 2).Value = "Points";
        fill(sheet);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static ParsedGradeFile Read(byte[] bytes, long max = 5 * 1024 * 1024) => GradeColumnImporter.Read(new MemoryStream(bytes), max, default);

    [Fact]
    public void A_plain_xlsx_is_read_with_numbers_text_numbers_and_blank_points()
    {
        var file = Read(Workbook(s =>
        {
            s.Cell(2, 1).Value = "S1"; s.Cell(2, 2).Value = 7.5;
            s.Cell(3, 1).Value = "S2"; s.Cell(3, 2).Value = "8,25";
            s.Cell(4, 1).Value = "S3";
            s.Cell(5, 1).Value = "S4"; s.Cell(5, 2).Value = "abc";
            s.Cell(6, 1).Value = string.Empty; s.Cell(6, 2).Value = 3;
        }));

        Assert.Null(file.FileError);
        Assert.Equal((7.5m, null), (file.Rows[0].Points, file.Rows[0].ErrorCode));
        Assert.Equal(8.25m, file.Rows[1].Points);
        Assert.Equal((null, null), (file.Rows[2].Points, file.Rows[2].ErrorCode)); // blank points: no change
        Assert.Equal("points_not_a_number", file.Rows[3].ErrorCode);
        Assert.Equal("student_number_required", file.Rows[4].ErrorCode);
    }

    [Fact]
    public void Import_accepts_xlsx_only_and_refuses_macro_files_and_other_formats()
    {
        Assert.Equal("not_xlsx", Read([0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3, 4]).FileError); // the old binary .xls
        Assert.Equal("not_xlsx", Read("StudentNumber,Points\nS1,5"u8.ToArray()).FileError); // csv
        Assert.Equal("not_xlsx", Read([.. "PK\u0003\u0004"u8, 1, 2, 3]).FileError); // a zip header with no workbook

        var withMacro = AddPart(Workbook(s => s.Cell(2, 1).Value = "S1"), "xl/vbaProject.bin", [1, 2, 3]);
        Assert.Equal("not_xlsx", Read(withMacro).FileError);
        var macroType = ReplaceContentTypes(Workbook(s => s.Cell(2, 1).Value = "S1"), "application/vnd.ms-excel.sheet.macroEnabled.main+xml");
        Assert.Equal("not_xlsx", Read(macroType).FileError);
        Assert.Equal("not_xlsx", Read(AddPart(Workbook(_ => { }), "xl/embeddings/oleObject1.bin", [1])).FileError);
    }

    [Fact]
    public void Import_respects_size_row_and_column_limits()
    {
        Assert.Equal("too_large", Read(Workbook(s => s.Cell(2, 1).Value = "S1"), max: 100).FileError);

        var manyRows = Workbook(s => { for (var r = 2; r <= GradingLimits.ImportMaxRows + 2; r++) { s.Cell(r, 1).Value = "S" + r; s.Cell(r, 2).Value = 1; } });
        Assert.Equal("too_many_rows", Read(manyRows).FileError);
        var atLimit = Workbook(s => { for (var r = 2; r <= GradingLimits.ImportMaxRows + 1; r++) { s.Cell(r, 1).Value = "S" + r; s.Cell(r, 2).Value = 1; } });
        Assert.Null(Read(atLimit).FileError);

        var manyColumns = Workbook(s => s.Cell(1, GradingLimits.ImportMaxColumns + 1).Value = "Extra");
        Assert.Equal("too_many_columns", Read(manyColumns).FileError);
        Assert.Equal("missing_columns", Read(Workbook(s => s.Cell(1, 2).Value = "Other")).FileError);
        Assert.Equal(TimeSpan.FromSeconds(30), GradeColumnImporter.Budget);
    }

    [Fact]
    public void A_small_file_that_expands_hugely_is_refused()
    {
        // ~30 MB of spaces deflate to a few tens of KB: the upload is small, the inflated parts are not
        var bomb = ReplacePart(Workbook(s => s.Cell(2, 1).Value = "S1"), "xl/worksheets/sheet1.xml", new string(' ', 30 * 1024 * 1024));

        Assert.True(bomb.Length < 200_000, $"the test file is {bomb.Length} bytes");
        Assert.Equal("not_xlsx", Read(bomb, max: 5 * 1024 * 1024).FileError);
    }

    [Fact]
    public void A_single_part_with_a_huge_ratio_is_refused_even_under_the_total_cap()
    {
        var ratio = ReplacePart(Workbook(s => s.Cell(2, 1).Value = "S1"), "xl/worksheets/sheet1.xml", new string(' ', 8 * 1024 * 1024));

        Assert.Equal("not_xlsx", Read(ratio).FileError); // 8 MB inflated from a few KB: ratio far above 100:1
    }

    [Fact]
    public void Too_many_zip_entries_are_refused()
    {
        var bytes = Workbook(s => s.Cell(2, 1).Value = "S1");
        for (var i = 0; i < GradeColumnImporter.MaxEntries + 1; i++)
        {
            bytes = AddPart(bytes, $"docProps/extra{i}.txt", [65]);
        }

        Assert.Equal("not_xlsx", Read(bytes).FileError);
    }

    [Fact]
    public void A_long_student_number_is_a_row_error_and_is_never_echoed_and_a_huge_number_is_a_row_error()
    {
        var file = Read(Workbook(s =>
        {
            s.Cell(2, 1).Value = new string('9', 32_000); s.Cell(2, 2).Value = 5;
            s.Cell(3, 1).Value = "S1"; s.Cell(3, 2).Value = 1e30;
            s.Cell(4, 1).Value = new string('7', GradeColumnImporter.MaxStudentNumberLength); s.Cell(4, 2).Value = 2;
        }));

        Assert.Null(file.FileError);
        Assert.Equal(("student_number_invalid", string.Empty), (file.Rows[0].ErrorCode, file.Rows[0].StudentNumber));
        Assert.Equal(("points_not_a_number", null), (file.Rows[1].ErrorCode, file.Rows[1].Points)); // not an unreadable file
        Assert.Null(file.Rows[2].ErrorCode); // exactly at the limit is fine
    }

    [Fact]
    public void A_hidden_first_sheet_is_never_read()
    {
        using var workbook = new XLWorkbook();
        var hidden = workbook.Worksheets.Add("Secret");
        hidden.Cell(1, 1).Value = "StudentNumber"; hidden.Cell(1, 2).Value = "Points"; hidden.Cell(2, 1).Value = "HIDDEN"; hidden.Cell(2, 2).Value = 1;
        hidden.Visibility = XLWorksheetVisibility.Hidden;
        var visible = workbook.Worksheets.Add("Grades");
        visible.Cell(1, 1).Value = "StudentNumber"; visible.Cell(1, 2).Value = "Points"; visible.Cell(2, 1).Value = "S1"; visible.Cell(2, 2).Value = 2;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var file = Read(stream.ToArray());

        Assert.Equal("S1", Assert.Single(file.Rows).StudentNumber);
    }

    [Fact]
    public void A_formula_cell_is_an_error_and_is_never_evaluated()
    {
        var file = Read(Workbook(s =>
        {
            s.Cell(2, 1).Value = "S1"; s.Cell(2, 2).FormulaA1 = "=1+1";
            s.Cell(3, 1).FormulaA1 = "=\"S\"&\"2\""; s.Cell(3, 2).Value = 4;
        }));

        Assert.All(file.Rows, r => { Assert.Equal("formula_not_allowed", r.ErrorCode); Assert.Null(r.Points); });
    }

    [Fact]
    public void Import_ignores_external_links_and_defined_names()
    {
        var bytes = AddPart(Workbook(s => { s.Cell(2, 1).Value = "S1"; s.Cell(2, 2).Value = 5; }), "xl/externalLinks/externalLink1.xml", [60, 47, 62]);

        var file = Read(bytes);

        Assert.Null(file.FileError);
        Assert.Equal(5m, Assert.Single(file.Rows).Points); // read from the sheet; the link part is never followed
    }

    [Fact]
    public void The_preview_token_is_bound_expires_and_cannot_be_forged()
    {
        var tokens = new ImportPreviewToken(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("t10"));
        var payload = new ImportTokenPayload(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ImportPreviewToken.Sha256([1, 2, 3]), [new TokenRow(Guid.NewGuid(), 5, null)]);

        var (token, expires) = tokens.Issue(payload);

        Assert.True(expires > DateTime.UtcNow.AddMinutes(GradingLimits.ImportTokenMinutes - 1));
        Assert.Equal(payload.ItemId, tokens.Read(token)!.ItemId);
        Assert.Null(tokens.Read(token + "x"));
        Assert.Null(tokens.Read("not a token"));
        // another application key never reads it
        Assert.Null(new ImportPreviewToken(Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("other")).Read(token));
        Assert.NotEqual(ImportPreviewToken.Sha256([1, 2, 3]), ImportPreviewToken.Sha256([1, 2, 4]));
    }

    private static byte[] AddPart(byte[] xlsx, string name, byte[] content)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var source = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read))
            {
                foreach (var entry in source.Entries)
                {
                    using var from = entry.Open();
                    using var to = zip.CreateEntry(entry.FullName).Open();
                    from.CopyTo(to);
                }
            }

            using var extra = zip.CreateEntry(name).Open();
            extra.Write(content);
        }

        return output.ToArray();
    }

    private static byte[] ReplacePart(byte[] xlsx, string name, string newContent)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var source = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read))
        {
            foreach (var entry in source.Entries)
            {
                using var from = entry.Open();
                using var to = zip.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.SmallestSize).Open();
                if (entry.FullName == name)
                {
                    to.Write(System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><worksheet><sheetData/><!--" + newContent + "--></worksheet>"));
                }
                else
                {
                    from.CopyTo(to);
                }
            }
        }

        return output.ToArray();
    }

    private static byte[] ReplaceContentTypes(byte[] xlsx, string workbookType)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var source = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read))
        {
            foreach (var entry in source.Entries)
            {
                using var from = entry.Open();
                using var to = zip.CreateEntry(entry.FullName).Open();
                if (entry.FullName == "[Content_Types].xml")
                {
                    var text = new StreamReader(from).ReadToEnd().Replace("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", workbookType);
                    to.Write(System.Text.Encoding.UTF8.GetBytes(text));
                }
                else
                {
                    from.CopyTo(to);
                }
            }
        }

        return output.ToArray();
    }
}
