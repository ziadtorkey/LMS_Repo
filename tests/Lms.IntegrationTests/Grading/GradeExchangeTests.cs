using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Lms.Domain.Auditing;
using Lms.Domain.Grading;
using Lms.Domain.People;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Grading.GradebookGridHost;
using static Lms.IntegrationTests.Grading.GradebookTestKit;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 10: grade column export, import preview and apply (FR-GRD-04, L9).</summary>
[Collection(ApiCollection.Name)]
public sealed class GradeExchangeTests(LmsApiFactory factory)
{
    private static byte[] Sheet(params (string Number, object? Points)[] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Grades");
        sheet.Cell(1, 1).Value = "StudentNumber";
        sheet.Cell(1, 2).Value = "Points";
        for (var i = 0; i < rows.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].Number;
            switch (rows[i].Points)
            {
                case double d: sheet.Cell(i + 2, 2).Value = d; break;
                case string s: sheet.Cell(i + 2, 2).Value = s; break;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName = "grades.xlsx")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        return content;
    }

    private async Task<HttpResponseMessage> PreviewAsync(GradebookGridHost host, GradebookGridHost.World w, Lms.Domain.Access.RoleAssignment caller, Guid item, byte[] bytes, string fileName = "grades.xlsx")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Routes.ClassCourseGradebookItemImportPreview(w.Gb.ClassCourse.Id, item)) { Content = Upload(bytes, fileName) };
        return await host.Host.SendRawAsync(request, caller);
    }

    private static Task<HttpResponseMessage> ApplyAsync(GradebookGridHost host, GradebookGridHost.World w, Lms.Domain.Access.RoleAssignment caller, Guid item, string token) =>
        host.SendAsync(caller, HttpMethod.Post, Routes.ClassCourseGradebookItemImportApply(w.Gb.ClassCourse.Id, item), new ApplyGradeImportRequest(token));

    private static async Task<GradeImportPreviewDto> PreviewOkAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GradeImportPreviewDto>())!;
    }

    private async Task<List<AuditEntry>> AuditAsync(Guid orgId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AuditEntry>().Where(a => a.OrganizationId == orgId).ToListAsync();
    }

    private static string Code(GradebookGridHost.World w, int i) => w.Students[i].StudentCode;

    [Fact]
    public async Task Export_is_keyed_by_student_number_escapes_cells_and_is_audited()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX1");
        var item = await host.AddItemAsync(w, "Paper", 20);
        await host.EntriesAsync(w, w.Students[0].Id, 0, new GradeEntryChange(item.Id, GradeEntryActions.Set, 12.5m, null));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var s = await db.Set<StudentProfile>().SingleAsync(x => x.Id == w.Students[1].Id);
            s.Update("=cmd|' /C calc'!A0", "+Evil", null, null, new DateOnly(2015, 5, 5), "female", "muslim", "EG", null, null, null, null);
            await db.SaveChangesAsync();
        }

        var response = await host.SendAsync(w.Admin, HttpMethod.Get, Routes.ClassCourseGradebookItemExport(w.Gb.ClassCourse.Id, item.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet(1);
        var rows = Enumerable.Range(2, 3).Select(r => (Code: sheet.Cell(r, 1).GetString(), Name: sheet.Cell(r, 2), Points: sheet.Cell(r, 4))).ToList();
        Assert.Equal(w.Students.Select(s => s.StudentCode).Order(StringComparer.Ordinal), rows.Select(r => r.Code));
        Assert.Equal(12.5, rows.Single(r => r.Code == Code(w, 0)).Points.GetDouble());
        Assert.True(rows.Single(r => r.Code == Code(w, 1)).Name.Style.IncludeQuotePrefix); // "+Evil" is text, never a formula
        Assert.All(rows, r => Assert.False(r.Name.HasFormula));
        Assert.Contains(await AuditAsync(w.Tree.Org.Id), a => (a.EntityType + a.AfterJson).Contains("Grades.ColumnExported", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_then_apply_writes_manual_scores_and_audits_both()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX2");
        var item = await host.AddItemAsync(w, "Paper", 20);
        await host.EntriesAsync(w, w.Students[0].Id, 0, new GradeEntryChange(item.Id, GradeEntryActions.Set, 5, null));

        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 15d), (Code(w, 1), "8,5"), (Code(w, 2), null))));

        Assert.Equal((3, 1), (preview.TotalRows, preview.Overwrites)); // student 0 already had a score; student 2's blank cell changes nothing
        Assert.Empty(preview.UnknownCodes);
        Assert.Empty(preview.OutOfRange);
        Assert.Empty(preview.Errors);
        Assert.True(preview.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(GradingLimits.ImportTokenMinutes - 1));
        Assert.Equal(5m, GradebookGridHost.Cell((await host.GridAsync(w)).Rows.Items.Single(r => r.StudentId == w.Students[0].Id), item.Id).Points); // a preview writes nothing

        var applied = await ApplyAsync(host, w, w.Admin, item.Id, preview.Token);

        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var result = (await applied.Content.ReadFromJsonAsync<GradeImportResultDto>())!;
        Assert.Equal((2, 0), (result.Written, result.Skipped.Count));
        var grid = await host.GridAsync(w);
        Assert.Equal(15m, GradebookGridHost.Cell(grid.Rows.Items.Single(r => r.StudentId == w.Students[0].Id), item.Id).Points);
        Assert.Equal(8.5m, GradebookGridHost.Cell(grid.Rows.Items.Single(r => r.StudentId == w.Students[1].Id), item.Id).Points);
        Assert.Equal(GradeCellStates.Empty, GradebookGridHost.Cell(grid.Rows.Items.Single(r => r.StudentId == w.Students[2].Id), item.Id).State);
        var audit = await AuditAsync(w.Tree.Org.Id);
        Assert.Contains(audit, a => (a.EntityType + a.AfterJson).Contains("Grades.ImportPreviewed", StringComparison.Ordinal));
        Assert.Contains(audit, a => (a.EntityType + a.AfterJson).Contains("Grades.ImportApplied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Preview_reports_unknown_codes_duplicates_out_of_range_and_bad_cells_without_blocking_the_rest()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX3");
        var item = await host.AddItemAsync(w, "Paper", 20);

        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet(
            (Code(w, 0), 10d), ("NOPE", 5d), (Code(w, 0), 11d), (Code(w, 1), 21d), (Code(w, 2), "abc"))));

        Assert.Equal(["NOPE"], preview.UnknownCodes);
        Assert.Equal(Code(w, 1), Assert.Single(preview.OutOfRange).StudentNumber);
        Assert.Contains(preview.Errors, e => e.ErrorCode == "duplicate_student");
        Assert.Contains(preview.Errors, e => e.ErrorCode == "points_not_a_number");
        var applied = (await (await ApplyAsync(host, w, w.Admin, item.Id, preview.Token)).Content.ReadFromJsonAsync<GradeImportResultDto>())!;
        Assert.Equal(1, applied.Written); // only the one valid row
    }

    [Fact]
    public async Task Import_never_touches_event_scores()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX4");
        await host.RecordEventAsync(w, w.Students[0].Id, Guid.NewGuid(), 6, 1, At(YearStart.AddDays(3)));
        var eventItem = (await host.GridAsync(w)).Items.Single(i => !i.IsManual);

        Assert.Equal(HttpStatusCode.BadRequest, (await PreviewAsync(host, w, w.Admin, eventItem.Id, Sheet((Code(w, 0), 9d)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Admin, HttpMethod.Get, Routes.ClassCourseGradebookItemExport(w.Gb.ClassCourse.Id, eventItem.Id))).StatusCode); // exporting it is fine
        Assert.Equal(6m, GradebookGridHost.Cell((await host.GridAsync(w)).Rows.Items.Single(r => r.StudentId == w.Students[0].Id), eventItem.Id).Points);
    }

    [Fact]
    public async Task Import_accepts_xlsx_only_and_refuses_macro_files_and_bad_content()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX5");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var good = Sheet((Code(w, 0), 1d));

        async Task<(HttpStatusCode, string?)> TryAsync(byte[] bytes, string name)
        {
            var response = await PreviewAsync(host, w, w.Admin, item.Id, bytes, name);
            return (response.StatusCode, response.StatusCode == HttpStatusCode.BadRequest ? await CodeAsync(response) : null);
        }

        Assert.Equal((HttpStatusCode.OK, null), await TryAsync(good, "grades.xlsx"));
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), await TryAsync(good, "grades.xlsm"));
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), await TryAsync(good, "grades.xls"));
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), await TryAsync(good, "grades.csv"));
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), await TryAsync([0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3], "grades.xlsx")); // an .xls renamed
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), await TryAsync("StudentNumber,Points\nS1,5"u8.ToArray(), "grades.xlsx")); // csv renamed
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), await TryAsync([], "grades.xlsx"));

        using var noFile = new HttpRequestMessage(HttpMethod.Post, Routes.ClassCourseGradebookItemImportPreview(w.Gb.ClassCourse.Id, item.Id)) { Content = new MultipartFormDataContent() };
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Host.SendRawAsync(noFile, w.Admin)).StatusCode);
    }

    [Fact]
    public async Task Import_respects_the_row_limit_end_to_end()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX6");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var rows = Enumerable.Range(0, GradingLimits.ImportMaxRows + 1).Select(i => ("S" + i, (object?)1d)).ToArray();

        var response = await PreviewAsync(host, w, w.Admin, item.Id, Sheet(rows));

        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_invalid"), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task Preview_token_is_bound_to_uploader_gradebook_item_and_expired_or_foreign_tokens_are_refused()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX7");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var other = await host.AddItemAsync(w, "Other", 20);
        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 3d))));

        async Task<(HttpStatusCode, string?)> TryAsync(GradebookGridHost.World world, Lms.Domain.Access.RoleAssignment who, Guid itemId, string token)
        {
            var response = await ApplyAsync(host, world, who, itemId, token);
            return (response.StatusCode, response.StatusCode == HttpStatusCode.BadRequest ? await CodeAsync(response) : null);
        }

        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_expired"), await TryAsync(w, w.Teacher, item.Id, preview.Token)); // another uploader
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_expired"), await TryAsync(w, w.Admin, other.Id, preview.Token)); // another item
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_expired"), await TryAsync(w, w.Admin, item.Id, preview.Token + "x")); // tampered
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_expired"), await TryAsync(w, w.Admin, item.Id, "garbage"));
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.Admin, HttpMethod.Post, Routes.ClassCourseGradebookItemImportApply(w.Gb.ClassCourse.Id, item.Id), new ApplyGradeImportRequest(" "))).StatusCode);
        Assert.Equal(GradeCellStates.Empty, GradebookGridHost.Cell((await host.GridAsync(w)).Rows.Items.First(r => r.StudentId == w.Students[0].Id), item.Id).State);

        // a token of another gradebook (another class course, same user) is refused too
        var second = await host.SeedAsync("GX7B");
        var foreignItem = await host.AddItemAsync(second, "Paper", 20);
        var foreign = await PreviewOkAsync(await PreviewAsync(host, second, second.Admin, foreignItem.Id, Sheet((Code(second, 0), 3d))));
        Assert.Equal((HttpStatusCode.BadRequest, "grade_import_expired"), await TryAsync(w, w.Admin, item.Id, foreign.Token));

        // the right user, gradebook and item applies it
        Assert.Equal(HttpStatusCode.OK, (await ApplyAsync(host, w, w.Admin, item.Id, preview.Token)).StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_be_applied_twice()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX12");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 7d), (Code(w, 1), 8d))));

        var first = (await (await ApplyAsync(host, w, w.Admin, item.Id, preview.Token)).Content.ReadFromJsonAsync<GradeImportResultDto>())!;
        // a teacher corrects a grade after the first apply
        var row = (await host.GridAsync(w)).Rows.Items.Single(r => r.StudentId == w.Students[0].Id);
        await host.EntriesAsync(w, w.Students[0].Id, row.Version, new GradeEntryChange(item.Id, GradeEntryActions.Set, 12, null));
        var second = (await (await ApplyAsync(host, w, w.Admin, item.Id, preview.Token)).Content.ReadFromJsonAsync<GradeImportResultDto>())!;

        Assert.Equal(2, first.Written);
        Assert.Equal(0, second.Written); // the replay overwrites nothing
        Assert.All(second.Skipped, s => Assert.Equal("changed_since_preview", s.ErrorCode));
        Assert.Equal(12m, GradebookGridHost.Cell((await host.GridAsync(w)).Rows.Items.Single(r => r.StudentId == w.Students[0].Id), item.Id).Points);
    }

    [Fact]
    public async Task A_grade_edited_after_the_preview_is_not_overwritten()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX13");
        var item = await host.AddItemAsync(w, "Paper", 20);
        await host.EntriesAsync(w, w.Students[0].Id, 0, new GradeEntryChange(item.Id, GradeEntryActions.Set, 5, null));
        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 15d), (Code(w, 1), 9d), (Code(w, 2), 3d))));

        // after the preview: student 0's grade is edited, and student 2 gets a first grade (an entry appears where the preview saw none)
        var row = (await host.GridAsync(w)).Rows.Items.Single(r => r.StudentId == w.Students[0].Id);
        await host.EntriesAsync(w, w.Students[0].Id, row.Version, new GradeEntryChange(item.Id, GradeEntryActions.Set, 6, null));
        await host.EntriesAsync(w, w.Students[2].Id, 0, new GradeEntryChange(item.Id, GradeEntryActions.Set, 1, null));

        var result = (await (await ApplyAsync(host, w, w.Admin, item.Id, preview.Token)).Content.ReadFromJsonAsync<GradeImportResultDto>())!;

        Assert.Equal(1, result.Written); // only student 1, whose cell did not change
        Assert.Equal(2, result.Skipped.Count(s => s.ErrorCode == "changed_since_preview"));
        var grid = await host.GridAsync(w);
        Assert.Equal(6m, GradebookGridHost.Cell(grid.Rows.Items.Single(r => r.StudentId == w.Students[0].Id), item.Id).Points);
        Assert.Equal(1m, GradebookGridHost.Cell(grid.Rows.Items.Single(r => r.StudentId == w.Students[2].Id), item.Id).Points);
        Assert.Equal(9m, GradebookGridHost.Cell(grid.Rows.Items.Single(r => r.StudentId == w.Students[1].Id), item.Id).Points);
    }

    [Fact]
    public async Task A_long_student_number_never_comes_back_in_the_preview()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX14");
        var item = await host.AddItemAsync(w, "Paper", 20);

        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((new string('9', 5000), 5d), (Code(w, 0), 1e30 > 0 ? "1E+30" : null))));

        Assert.Empty(preview.UnknownCodes);
        Assert.Contains(preview.Errors, e => e.ErrorCode == "student_number_invalid" && e.StudentNumber.Length == 0);
        Assert.Contains(preview.Errors, e => e.ErrorCode == "points_not_a_number" || e.ErrorCode == "points_not_a_number");
    }

    [Fact]
    public async Task Apply_revalidates_against_current_data()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX8");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 15d), (Code(w, 1), 4d), (Code(w, 2), 6d))));

        // after the preview: the item shrinks (15 no longer fits), a student leaves, another is excused
        var category = (await host.GridAsync(w)).Categories.Single().Id;
        await host.SendAsync(w.Admin, HttpMethod.Put, host.Url(w, "/items/" + item.Id), new UpdateGradeItemRequest(category, item.TitleAr, item.TitleEn, 10, 1));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            foreach (var e in db.Set<Lms.Domain.Enrollment.CourseEnrollment>().Where(e => e.StudentId == w.Students[1].Id))
            {
                e.Drop(new DateOnly(2026, 10, 5), "left");
            }

            await db.SaveChangesAsync();
        }

        await host.EntriesAsync(w, w.Students[2].Id, 0, new GradeEntryChange(item.Id, GradeEntryActions.Excuse, null, null));

        var result = (await (await ApplyAsync(host, w, w.Admin, item.Id, preview.Token)).Content.ReadFromJsonAsync<GradeImportResultDto>())!;

        Assert.Equal(0, result.Written);
        Assert.Equal(["excused", "grade_out_of_range", "unknown_student"], result.Skipped.Select(s => s.ErrorCode!).Order());
    }

    [Fact]
    public async Task A_gradebook_locked_after_the_preview_refuses_the_apply()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX9");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 3d))));
        var grid = await host.GridAsync(w);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Gradebook>().SingleAsync(g => g.Id == grid.Id)).Lock(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await ApplyAsync(host, w, w.Admin, item.Id, preview.Token);

        Assert.Equal((HttpStatusCode.Conflict, "gradebook_locked"), (response.StatusCode, await CodeAsync(response)));
    }

    [Fact]
    public async Task The_exchange_follows_the_permission_and_the_relationship()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX10");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var export = Routes.ClassCourseGradebookItemExport(w.Gb.ClassCourse.Id, item.Id);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Teacher, HttpMethod.Get, export)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Supervisor, HttpMethod.Get, export)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.OtherTeacher, HttpMethod.Get, export)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PreviewAsync(host, w, w.Supervisor, item.Id, Sheet((Code(w, 0), 1d)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PreviewAsync(host, w, w.OtherTeacher, item.Id, Sheet((Code(w, 0), 1d)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ApplyAsync(host, w, w.OtherTeacher, item.Id, "x")).StatusCode);
        // an item of another class course is a miss
        var b = await host.SeedAsync("GX10B");
        var foreignItem = await host.AddItemAsync(b, "Paper", 20);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.Admin, HttpMethod.Get, Routes.ClassCourseGradebookItemExport(w.Gb.ClassCourse.Id, foreignItem.Id))).StatusCode);
    }

    [Fact]
    public async Task Preview_and_apply_audit_entries_carry_counts_and_ids_but_no_grade_values()
    {
        using var host = new GradebookGridHost(factory);
        var w = await host.SeedAsync("GX11");
        var item = await host.AddItemAsync(w, "Paper", 20);
        var preview = await PreviewOkAsync(await PreviewAsync(host, w, w.Admin, item.Id, Sheet((Code(w, 0), 13.37d))));
        await ApplyAsync(host, w, w.Admin, item.Id, preview.Token);

        var events = (await AuditAsync(w.Tree.Org.Id)).Where(a => (a.EntityType + a.AfterJson).Contains("Grades.Import", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.DoesNotContain("13.37", (e.EntityType + e.AfterJson + e.BeforeJson), StringComparison.Ordinal));
    }
}
