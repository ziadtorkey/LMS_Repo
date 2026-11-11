using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Lms.Domain.Access;
using Lms.Domain.Imports;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Imports;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Imports;

/// <summary>Upload, validation job, preview, all-or-nothing commit job, error report and clean-up (M2 Task 7, X-06).</summary>
[Collection(ApiCollection.Name)]
public sealed class ImportFrameworkTests(LmsApiFactory factory) : IDisposable
{
    private const string Kind = TestImportDefinition.KindName;
    private readonly ImportTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    private static string Code(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private async Task<Guid> UploadValidatedAsync(RoleAssignment admin, Guid unitId, params string?[][] rows) =>
        await UploadValidatedAsync(admin, unitId, null, rows);

    private async Task<Guid> UploadValidatedAsync(RoleAssignment admin, Guid unitId, string? language, params string?[][] rows)
    {
        var response = await _host.UploadAsync(admin, unitId, XlsxBuilder.Create(XlsxBuilder.WidgetHeaders, rows), language: language);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();
        await _host.RunQueuedJobsAsync();
        return id;
    }

    [Fact]
    public async Task Template_has_data_and_instructions_sheets_in_requested_culture()
    {
        var (_, admin) = await SetUpAsync("IF1");

        var response = await _host.SendAsync(HttpMethod.Get, Routes.ImportTemplate(Kind, "ar"), admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"{Kind}-template.xlsx", response.Content.Headers.ContentDisposition!.FileName);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        Assert.Equal(["Data", "Instructions"], workbook.Worksheets.Select(w => w.Name));
        var data = workbook.Worksheet("Data");
        Assert.Equal("الرمز", data.Cell(1, 1).GetString());
        Assert.Equal("الاسم", data.Cell(1, 2).GetString());
        Assert.True(data.Cell(1, 1).Style.Font.Bold);
        Assert.Equal("Code", workbook.Worksheet("Instructions").Cell(2, 1).GetString());

        var english = await _host.SendAsync(HttpMethod.Get, Routes.ImportTemplate(Kind, "en"), admin);
        using var enBook = new XLWorkbook(await english.Content.ReadAsStreamAsync());
        Assert.Equal("Code", enBook.Worksheet("Data").Cell(1, 1).GetString());
    }

    [Fact]
    public async Task Columns_endpoint_lists_the_definition_columns()
    {
        var (_, admin) = await SetUpAsync("IF2");

        var response = await _host.SendAsync(HttpMethod.Get, Routes.ImportColumns(Kind), admin);

        var columns = (await response.Content.ReadFromJsonAsync<List<ImportColumnDto>>())!;
        Assert.Equal(["code", "name", "grade", "start", "unit"], columns.Select(c => c.Key));
        Assert.Equal(["A", "B"], columns.Single(c => c.Key == "grade").AllowedValues);
    }

    [Fact]
    public async Task Valid_file_reaches_Valid_with_preview_rows()
    {
        var (tree, admin) = await SetUpAsync("IF3");
        var a = Code("A");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [a, "First", "A", "2026-09-01", null], [Code("B"), "Second", null, null, null]);

        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Valid", state.Status);
        Assert.Equal(2, state.RowCount);
        Assert.Equal(0, state.ErrorCount);

        var batch = (await _host.SendAsync(HttpMethod.Get, Routes.ImportBatch(Kind, id), admin)).Content;
        Assert.Equal("Valid", (await JsonAsync(new HttpResponseMessage { Content = batch })).GetProperty("status").GetString());
        var rows = await _host.SendAsync(HttpMethod.Get, Routes.ImportRows(Kind, id), admin);
        var page = (await JsonAsync(rows)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([2, 3], page.Select(r => r.GetProperty("rowNumber").GetInt32()));
        Assert.Equal(a, page[0].GetProperty("values").GetProperty("code").GetString());
        Assert.Empty(page[0].GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Invalid_file_reaches_Invalid_with_error_report_highlighting_cells()
    {
        var (tree, admin) = await SetUpAsync("IF4");
        var dup = Code("D");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, "en",
            [dup, "One", "A", null, null], [dup, "Two", "Z", "not-a-date", "NOPE"], [Code("E"), null, null, null, null]);

        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Invalid", state.Status);
        Assert.NotNull(state.ErrorReportFileId);
        Assert.True(state.ErrorCount >= 5);

        var report = await _host.SendAsync(HttpMethod.Get, Routes.ImportErrorReport(Kind, id), admin);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        Assert.Equal($"{Kind}-errors-{id:N}.xlsx", report.Content.Headers.ContentDisposition!.FileName);
        using var workbook = new XLWorkbook(await report.Content.ReadAsStreamAsync());
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Errors", sheet.Cell(1, 6).GetString());
        Assert.Equal("", sheet.Cell(2, 6).GetString()); // the first duplicate row is fine
        var flagged = XLColor.FromHtml("#FFC7CE");
        Assert.Equal(flagged, sheet.Cell(3, 3).Style.Fill.BackgroundColor); // grade "Z" not allowed
        Assert.Equal(flagged, sheet.Cell(3, 4).Style.Fill.BackgroundColor); // bad date
        Assert.Equal(flagged, sheet.Cell(3, 5).Style.Fill.BackgroundColor); // unknown unit
        Assert.Equal(flagged, sheet.Cell(3, 1).Style.Fill.BackgroundColor); // duplicate code
        Assert.Equal(flagged, sheet.Cell(4, 2).Style.Fill.BackgroundColor); // name required
        Assert.Contains("Same value as row 2", sheet.Cell(3, 6).GetString());

        var errorRows = await _host.SendAsync(HttpMethod.Get, Routes.ImportRows(Kind, id) + "?errorsOnly=true&culture=ar", admin);
        var items = (await JsonAsync(errorRows)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([3, 4], items.Select(r => r.GetProperty("rowNumber").GetInt32()));
        Assert.Contains(items[1].GetProperty("errors").EnumerateArray(), e => e.GetProperty("message").GetString() == "هذه القيمة مطلوبة.");
    }

    [Fact]
    public async Task Commit_creates_the_records_sends_after_commit_and_notifies_the_requester()
    {
        var (tree, admin) = await SetUpAsync("IF5");
        var a = Code("OK");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [a, "Alpha", null, null, null]);

        var commit = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        Assert.Equal(HttpStatusCode.Accepted, commit.StatusCode);
        await _host.RunQueuedJobsAsync();

        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Committed", state.Status);
        Assert.Equal(1, state.CreatedCount);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.OrgUnits.AnyAsync(u => u.OrganizationId == tree.Org.Id && u.Code == a));
        Assert.Contains(a, TestImportDefinition.Sent);
        var notified = await db.Set<Notification>().Where(n => n.UserId == admin.UserId && n.EventKey == "Imports.Completed").ToListAsync();
        Assert.Equal(2, notified.Count); // after validation and after commit
    }

    [Fact]
    public async Task Commit_is_all_or_nothing_when_the_last_row_throws()
    {
        var (tree, admin) = await SetUpAsync("IF6");
        var first = Code("OK");
        var boom = Code("BOOM");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [first, "One", null, null, null], [boom, "Two", null, null, null]);
        Assert.Equal("Valid", (await _host.BatchAsync(factory, id)).Status);

        await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        await _host.RunQueuedJobsAsync();

        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Failed", state.Status);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.OrgUnits.AnyAsync(u => u.OrganizationId == tree.Org.Id && (u.Code == first || u.Code == boom)));
        Assert.DoesNotContain(first, TestImportDefinition.Sent);
        Assert.DoesNotContain(boom, TestImportDefinition.Sent);
        var batch = await db.Set<ImportBatch>().SingleAsync(b => b.Id == id);
        Assert.Equal("unexpected", batch.FailureCode);
    }

    [Fact]
    public async Task Commit_revalidates_and_fails_when_data_changed_after_preview()
    {
        var (tree, admin) = await SetUpAsync("IF7");
        var code = Code("TK");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [code, "One", null, null, null]);
        Assert.Equal("Valid", (await _host.BatchAsync(factory, id)).Status);

        TestImportDefinition.Taken[code.ToUpperInvariant()] = true; // someone else created it after the preview
        await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        await _host.RunQueuedJobsAsync();

        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Invalid", state.Status);
        Assert.NotNull(state.ErrorReportFileId);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.OrgUnits.AnyAsync(u => u.OrganizationId == tree.Org.Id && u.Code == code));
    }

    [Fact]
    public async Task Second_commit_call_is_409_and_commits_once()
    {
        var (tree, admin) = await SetUpAsync("IF8");
        var code = Code("TW");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [code, "One", null, null, null]);

        var first = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        var second = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(ProblemCodes.import_in_progress, (await JsonAsync(second)).GetProperty("code").GetString());
        await _host.RunQueuedJobsAsync();
        var third = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);
        Assert.Equal(ProblemCodes.import_already_committed, (await JsonAsync(third)).GetProperty("code").GetString());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.OrgUnits.CountAsync(u => u.OrganizationId == tree.Org.Id && u.Code == code));
    }

    [Fact]
    public async Task Commit_of_Invalid_batch_is_409_import_not_valid()
    {
        var (tree, admin) = await SetUpAsync("IF9");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [Code("X"), null, null, null, null]);

        var commit = await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, id), admin);

        Assert.Equal(HttpStatusCode.Conflict, commit.StatusCode);
        Assert.Equal(ProblemCodes.import_not_valid, (await JsonAsync(commit)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Too_many_rows_and_non_xlsx_are_rejected()
    {
        var (tree, admin) = await SetUpAsync("IF10");
        var many = Enumerable.Range(0, ImportLimits.MaxRows + 1).Select(i => new string?[] { "m" + i, "n", null, null, null }).ToArray();

        var notExcel = await _host.UploadAsync(admin, tree.School1.Id, [1, 2, 3], "widgets.csv");
        var empty = await _host.UploadAsync(admin, tree.School1.Id, [], "widgets.xlsx");
        Assert.Equal(HttpStatusCode.BadRequest, notExcel.StatusCode);
        Assert.Equal(ProblemCodes.import_file_invalid, (await JsonAsync(notExcel)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var id = await UploadValidatedAsync(admin, tree.School1.Id, many);
        var state = await _host.BatchAsync(factory, id);
        Assert.Equal("Invalid", state.Status);
        Assert.Equal(1, state.ErrorCount); // one file-level too_many_rows error
    }

    [Fact]
    public async Task Upload_into_other_subtree_unit_is_404_and_without_permission_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "IF11");
        var school1Admin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var student = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, Guid.NewGuid());
        var file = XlsxBuilder.Create(XlsxBuilder.WidgetHeaders, [Code("U"), "n", null, null, null]);

        var outside = await _host.UploadAsync(school1Admin, tree.School2.Id, file);
        var refused = await _host.UploadAsync(student, tree.School1.Id, file);

        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [Fact]
    public async Task Discard_removes_an_uncommitted_batch_and_refuses_a_committed_one()
    {
        var (tree, admin) = await SetUpAsync("IF12");
        var id = await UploadValidatedAsync(admin, tree.School1.Id, [Code("DS"), "n", null, null, null]);
        var done = await UploadValidatedAsync(admin, tree.School1.Id, [Code("DT"), "n", null, null, null]);
        await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, done), admin);
        await _host.RunQueuedJobsAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await _host.SendAsync(HttpMethod.Delete, Routes.ImportBatch(Kind, id), admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.ImportBatch(Kind, id), admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _host.SendAsync(HttpMethod.Delete, Routes.ImportBatch(Kind, done), admin)).StatusCode);
    }

    [Fact]
    public async Task Cleanup_removes_stale_uncommitted_batches_and_files()
    {
        var (tree, admin) = await SetUpAsync("IF13");
        var stale = await UploadValidatedAsync(admin, tree.School1.Id, [Code("ST"), "n", null, null, null]);
        var fresh = await UploadValidatedAsync(admin, tree.School1.Id, [Code("FR"), "n", null, null, null]);
        var committed = await UploadValidatedAsync(admin, tree.School1.Id, [Code("CM"), "n", null, null, null]);
        await _host.SendAsync(HttpMethod.Post, Routes.ImportCommit(Kind, committed), admin);
        await _host.RunQueuedJobsAsync();
        Guid sourceFile;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE ImportBatches SET CreatedAt = {DateTime.UtcNow.AddDays(-8)} WHERE Id IN ({stale}, {committed})");
            sourceFile = (await db.Set<ImportBatch>().SingleAsync(b => b.Id == stale)).SourceFileId;
        }

        using (var scope = _host.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<Lms.Api.Infrastructure.HttpCurrentContext>();
            context.SetUser(Guid.NewGuid(), true);
            await scope.ServiceProvider.GetRequiredService<ImportCleanupJob>().RunAsync();
        }

        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await check.Set<ImportBatch>().AnyAsync(b => b.Id == stale));
        Assert.False(await check.Set<ImportRow>().AnyAsync(r => r.BatchId == stale));
        Assert.True(await check.Set<ImportBatch>().AnyAsync(b => b.Id == fresh));
        Assert.True(await check.Set<ImportBatch>().AnyAsync(b => b.Id == committed)); // never removed once committed
        Assert.False(await check.Set<Lms.Domain.Files.StoredFile>().AnyAsync(f => f.Id == sourceFile));
    }
}
