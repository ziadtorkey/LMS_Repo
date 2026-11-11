using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Templates;

[Collection(ApiCollection.Name)]
public sealed class TemplateIsolationTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Template_of_client_B_is_invisible()
    {
        var a = await TemplateEndpointsTests.SetUpAsync(factory, "TI1A");
        var b = await TemplateEndpointsTests.SetUpAsync(factory, "TI1B");
        var saved = await _host.SendAsync(HttpMethod.Post, Routes.Templates, a.Admin, new SaveTemplateRequest(
            a.Grade.Id, "قالب", "Shared one", "Shared", false, false, new Dictionary<string, string>()));
        saved.EnsureSuccessStatusCode();
        var id = (await saved.Content.ReadFromJsonAsync<OrgTemplateDetailDto>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!.Summary.Id;

        // Client B, even for a Shared template of A: not listed, not readable, not applicable, not deletable.
        var list = (await (await _host.SendAsync(HttpMethod.Get, Routes.Templates, b.Admin)).Content.ReadFromJsonAsync<List<OrgTemplateSummaryDto>>())!;
        Assert.DoesNotContain(list, t => t.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.Template(id), b.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Post, Routes.TemplateApply(id), b.Admin,
            new ApplyTemplateRequest(b.Tree.School2.Id, new Dictionary<string, string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.Template(id), b.Admin)).StatusCode);

        // Saving from another client's unit is a 404 too.
        Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Post, Routes.Templates, b.Admin, new SaveTemplateRequest(
            a.Grade.Id, "ق", "X", "Private", false, false, new Dictionary<string, string>()))).StatusCode);

        // The table's own query filter hides the row from B's context.
        await using var db = factory.CreateDbContext(TestCurrentContext.For(b.Tree.Org.Id, b.Tree.Root.Path));
        Assert.False(await db.Set<OrgTemplate>().AnyAsync(t => t.Id == id));
    }
}
