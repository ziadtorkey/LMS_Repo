using System.Net;
using System.Text;
using ClosedXML.Excel;
using Lms.Domain.Auditing;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Hosting;

namespace Lms.IntegrationTests.Audit;

[Collection(ApiCollection.Name)]
public sealed class AuditExportTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Csv_export_has_bom_and_arabic_names()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AuditViewerTests.AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Probe", AuditAction.Created);

        var response = await factory.CreateClient().SendAsync(
            AuditViewerTests.Request("/api/audit/export?format=csv&entityType=Probe", admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Matches(@"^audit-\d{8}-\d{4}\.csv$", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Contains(tree.Root.NameAr, text); // unit name column, Arabic intact
        Assert.Contains("Probe", text);
        Assert.Equal(2, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length); // header + one row
    }

    [Fact]
    public async Task Xlsx_export_opens_with_closedxml_and_has_header_row()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AuditViewerTests.AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Probe", AuditAction.Created);

        var response = await factory.CreateClient().SendAsync(
            AuditViewerTests.Request("/api/audit/export?format=xlsx&entityType=Probe", admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(@"^audit-\d{8}-\d{4}\.xlsx$", response.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var sheet = workbook.Worksheet(1);
        Assert.False(string.IsNullOrEmpty(sheet.Cell(1, 1).GetString()));
        Assert.Contains(sheet.Row(2).CellsUsed(), c => c.GetString() == "Probe");
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task Export_over_limit_is_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        for (var i = 0; i < 3; i++)
        {
            await AuditViewerTests.AddEntryAsync(factory, tree.Org.Id, tree.Root.Path, "Probe");
        }

        using var limited = factory.WithWebHostBuilder(b => b.UseSetting("Audit:ExportMaxRows", "2"));
        var response = await limited.CreateClient().SendAsync(
            AuditViewerTests.Request("/api/audit/export?format=csv&entityType=Probe", admin));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("export_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Unknown_format_is_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await factory.CreateClient().SendAsync(AuditViewerTests.Request("/api/audit/export?format=pdf", admin));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Manager_with_View_but_not_Export_gets_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE5");
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        var response = await factory.CreateClient().SendAsync(AuditViewerTests.Request("/api/audit/export?format=csv", manager));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
