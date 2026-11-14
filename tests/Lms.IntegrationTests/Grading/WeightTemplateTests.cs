using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Grading;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 4: weight templates (FR-GRD-03): the categories and weights a teacher applies to a gradebook; applying copies the values.</summary>
[Collection(ApiCollection.Name)]
public sealed class WeightTemplateTests(LmsApiFactory factory)
{
    private static SaveWeightTemplateRequest Template(string name = "T", params decimal[] weights) =>
        new("قالب " + name, "Template " + name,
            [.. (weights.Length == 0 ? [60m, 40m] : weights).Select((w, i) => new WeightTemplateItemDto(null, "فئة " + i, "Category " + i, w, i))]);

    private static async Task<WeightTemplateDto> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<WeightTemplateDto>())!;
    }

    [Fact]
    public async Task Weight_template_apply_copies_values_so_a_later_edit_changes_nothing_already_read()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("WT1");
        var unit = w.Tree.School1.Id;

        var created = await ReadAsync(await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitWeightTemplates(unit), Template("A")));
        var snapshot = await ReadAsync(await host.SendAsync(w.Teacher, HttpMethod.Get, Routes.WeightTemplate(unit, created.Id)));
        await host.SendAsync(w.SchoolAdmin, HttpMethod.Put, Routes.WeightTemplate(unit, created.Id), Template("A", 10m, 20m, 30m));
        var edited = await ReadAsync(await host.SendAsync(w.Teacher, HttpMethod.Get, Routes.WeightTemplate(unit, created.Id)));

        Assert.Equal(100m, snapshot.WeightsTotal);
        Assert.Equal([60m, 40m], snapshot.Items.Select(i => i.Weight));
        Assert.Equal([10m, 20m, 30m], edited.Items.Select(i => i.Weight));
        Assert.Equal(60m, edited.WeightsTotal); // a total other than 100 is a warning, not a refusal
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    public async Task A_weight_outside_0_to_100_is_refused(double weight)
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("WT2");

        var response = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitWeightTemplates(w.Tree.School1.Id), Template("X", (decimal)weight, 50m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("weights_invalid", await GradingScaleTestHost.CodeAsync(response));
    }

    [Fact]
    public async Task Teacher_cannot_configure_weight_templates_but_reads_the_ancestors()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("WT3");
        var rootTemplate = await ReadAsync(await host.SendAsync(w.RootAdmin, HttpMethod.Post, Routes.OrgUnitWeightTemplates(w.Tree.Root.Id), Template("Root")));
        var school = w.Tree.School1.Id;

        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Post, Routes.OrgUnitWeightTemplates(school), Template())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Put, Routes.WeightTemplate(school, rootTemplate.Id), Template())).StatusCode);
        var inherited = await ReadAsync(await host.SendAsync(w.Teacher, HttpMethod.Get, Routes.WeightTemplate(school, rootTemplate.Id)));
        Assert.Equal(w.Tree.Root.Id, inherited.InheritedFromUnitId);
        // an inherited template is read only below its unit
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Put, Routes.WeightTemplate(school, rootTemplate.Id), Template())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Delete, Routes.WeightTemplate(school, rootTemplate.Id))).StatusCode);
    }

    [Fact]
    public async Task Weight_templates_do_not_cross_clients_or_siblings()
    {
        using var host = new GradingScaleTestHost(factory);
        var a = await host.SeedAsync("WT4A");
        var b = await host.SeedAsync("WT4B");
        var sibling = await host.AdminAtAsync(a, a.Tree.School2);
        var own = await ReadAsync(await host.SendAsync(sibling, HttpMethod.Post, Routes.OrgUnitWeightTemplates(a.Tree.School2.Id), Template("S2")));
        var foreign = await ReadAsync(await host.SendAsync(b.RootAdmin, HttpMethod.Post, Routes.OrgUnitWeightTemplates(b.Tree.Root.Id), Template("B")));

        var seenBySchool1 = await host.SendAsync(a.SchoolAdmin, HttpMethod.Get, Routes.OrgUnitWeightTemplates(a.Tree.School1.Id));
        var ids = (await seenBySchool1.Content.ReadFromJsonAsync<List<WeightTemplateDto>>())!.Select(t => t.Id).ToList();

        Assert.DoesNotContain(own.Id, ids);
        Assert.DoesNotContain(foreign.Id, ids);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.RootAdmin, HttpMethod.Get, Routes.WeightTemplate(a.Tree.Root.Id, foreign.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.SchoolAdmin, HttpMethod.Get, Routes.WeightTemplate(a.Tree.School1.Id, own.Id))).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task A_template_needs_between_1_and_50_items(int count)
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("WT5");
        var request = new SaveWeightTemplateRequest("قالب", "T", [.. Enumerable.Range(0, count).Select(i => new WeightTemplateItemDto(null, "ف", "C", 1m, i))]);

        var response = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitWeightTemplates(w.Tree.School1.Id), request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
