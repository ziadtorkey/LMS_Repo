using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Grading;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Grading;

/// <summary>M5 Task 4: scales, bands, one default per unit, inheritance, templates and copies, in use (FR-GRD-02, D58, D59, L7).</summary>
[Collection(ApiCollection.Name)]
public sealed class GradingScaleTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Bands_must_cover_0_to_100_without_gaps_or_overlaps()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS1");
        var gap = GradingScaleTestHost.Scale(bands: [new(0, 0, 50, "F", null, null, null, null, null), new(1, 60, 100, "P", null, null, null, null, null)]);

        var response = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitGradingScales(w.Tree.School1.Id), gap);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("scale_bands_invalid", await GradingScaleTestHost.CodeAsync(response));
        Assert.Contains("\"kind\":\"Gap\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_get_update_and_delete_a_scale()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS2");

        var created = await host.CreateScaleAsync(w.SchoolAdmin, w.Tree.School1.Id);
        Assert.False(created.IsDefault);
        Assert.False(created.Provisional);
        Assert.Equal(2, created.Bands.Count);
        Assert.Null(created.InheritedFromUnitId);

        var put = await host.SendAsync(w.SchoolAdmin, HttpMethod.Put, Routes.GradingScale(w.Tree.School1.Id, created.Id),
            GradingScaleTestHost.Scale("Renamed", bands: [new(0, 0, 100, "ALL", null, null, null, null, null)]));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var updated = (await put.Content.ReadFromJsonAsync<GradingScaleDto>())!;
        Assert.Equal("Scale Renamed", updated.NameEn);
        Assert.Equal("ALL", Assert.Single(updated.Bands).Code);

        var delete = await host.SendAsync(w.SchoolAdmin, HttpMethod.Delete, Routes.GradingScale(w.Tree.School1.Id, created.Id));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var gone = await host.SendAsync(w.SchoolAdmin, HttpMethod.Get, Routes.GradingScale(w.Tree.School1.Id, created.Id));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task One_default_per_unit()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS3");
        var a = await host.CreateScaleAsync(w.SchoolAdmin, w.Tree.School1.Id, GradingScaleTestHost.Scale("A"));
        var b = await host.CreateScaleAsync(w.SchoolAdmin, w.Tree.School1.Id, GradingScaleTestHost.Scale("B"));

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.GradingScaleDefault(w.Tree.School1.Id, a.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.GradingScaleDefault(w.Tree.School1.Id, b.Id))).StatusCode);

        var own = (await host.ListAsync(w.SchoolAdmin, w.Tree.School1.Id)).Where(s => s.InheritedFromUnitId is null).ToList();
        Assert.Equal([b.Id], own.Where(s => s.IsDefault).Select(s => s.Id));
    }

    [Fact]
    public async Task Provisioning_creates_a_plain_default_and_three_provisional_templates()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS4");

        var scales = await host.StoredAsync(w.Tree.Root.Id);

        var plain = Assert.Single(scales, s => s.SeedKey == SaudiPresetSeed.PlainDefaultKey);
        Assert.True(plain.IsDefault);
        Assert.False(plain.Provisional);
        Assert.Equal(ScaleKinds.Percentage, plain.Kind);
        var templates = scales.Where(s => s.IsTemplate).ToList();
        Assert.Equal(3, templates.Count);
        Assert.All(templates, t => Assert.True(t.Provisional));
        Assert.Equal(
            new[] { SaudiPresetSeed.SchoolKey, SaudiPresetSeed.University5Key, SaudiPresetSeed.University4Key }.Order(),
            templates.Select(t => t.SeedKey!).Order());
    }

    [Fact]
    public async Task Saudi_templates_are_never_the_default()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS5");
        var template = (await host.ListAsync(w.RootAdmin, w.Tree.Root.Id)).First(s => s.IsTemplate);

        Assert.All((await host.ListAsync(w.RootAdmin, w.Tree.Root.Id)).Where(s => s.IsTemplate), t => Assert.False(t.IsDefault));
        var asDefault = await host.SendAsync(w.RootAdmin, HttpMethod.Post, Routes.GradingScaleDefault(w.Tree.Root.Id, template.Id));
        Assert.Equal(HttpStatusCode.NotFound, asDefault.StatusCode);
    }

    [Fact]
    public async Task Reconcile_twice_adds_nothing_and_never_recreates_a_deleted_default()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS6");
        Assert.Equal(0, await host.ReconcileAsync(w.Tree.Org.Id));

        var plain = (await host.ListAsync(w.RootAdmin, w.Tree.Root.Id)).Single(s => s.SeedKey == SaudiPresetSeed.PlainDefaultKey);
        Assert.Equal(HttpStatusCode.NoContent, (await host.SendAsync(w.RootAdmin, HttpMethod.Delete, Routes.GradingScale(w.Tree.Root.Id, plain.Id))).StatusCode);

        Assert.Equal(0, await host.ReconcileAsync(w.Tree.Org.Id));
        Assert.DoesNotContain(await host.ListAsync(w.RootAdmin, w.Tree.Root.Id), s => s.SeedKey == SaudiPresetSeed.PlainDefaultKey);
    }

    [Fact]
    public async Task Copy_of_a_template_stays_provisional_until_confirmed_or_edited()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS7");
        var school = w.Tree.School1.Id;

        var copy = await Copy(host, w, SaudiPresetSeed.SchoolKey);
        Assert.True(copy.Provisional);
        Assert.False(copy.IsTemplate);
        Assert.False(copy.IsDefault);
        Assert.Equal(5, copy.Bands.Count);

        var confirmed = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.GradingScaleConfirm(school, copy.Id));
        Assert.False((await confirmed.Content.ReadFromJsonAsync<GradingScaleDto>())!.Provisional);

        var second = await Copy(host, w, SaudiPresetSeed.University4Key);
        Assert.True(second.Provisional);
        var request = GradingScaleTestHost.Scale("My GPA", ScaleKinds.Gpa, [.. second.Bands.Select(b =>
            new SaveGradingScaleBandRequest(b.Order, b.MinPercent, b.MaxPercent, b.Code, b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points))]);
        var edited = await host.SendAsync(w.SchoolAdmin, HttpMethod.Put, Routes.GradingScale(school, second.Id), request);
        Assert.False((await edited.Content.ReadFromJsonAsync<GradingScaleDto>())!.Provisional);
    }

    [Fact]
    public async Task Seed_version_bump_marks_unedited_copies_update_available_and_never_touches_edited_copies()
    {
        using var catalog = new MutableCatalog();
        using var host = new GradingScaleTestHost(factory, s => catalog.Install(s));
        var w = await host.SeedAsync("GS8");
        var school = w.Tree.School1.Id;
        var unedited = await Copy(host, w, SaudiPresetSeed.SchoolKey);
        var edited = await Copy(host, w, SaudiPresetSeed.University5Key);
        var request = GradingScaleTestHost.Scale("Mine", ScaleKinds.Gpa, [.. edited.Bands.Select(b =>
            new SaveGradingScaleBandRequest(b.Order, b.MinPercent, b.MaxPercent, b.Code, b.LabelAr, b.LabelEn, b.DescriptorAr, b.DescriptorEn, b.Points))]);
        await host.SendAsync(w.SchoolAdmin, HttpMethod.Put, Routes.GradingScale(school, edited.Id), request with { NameEn = "Mine" });
        Assert.All(await host.ListAsync(w.SchoolAdmin, school), s => Assert.False(s.UpdateAvailable));

        catalog.Bump();
        await host.ReconcileAsync(w.Tree.Org.Id);

        var list = await host.ListAsync(w.SchoolAdmin, school);
        Assert.True(list.Single(s => s.Id == unedited.Id).UpdateAvailable);
        Assert.False(list.Single(s => s.Id == edited.Id).UpdateAvailable);
        Assert.Equal(1, list.Single(s => s.Id == unedited.Id).SeedVersion); // the reconciler leaves copies alone

        var applied = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitGradingScalePresets(school), new CopyScalePresetRequest(SaudiPresetSeed.SchoolKey));
        var after = (await applied.Content.ReadFromJsonAsync<GradingScaleDto>())!;
        Assert.Equal(unedited.Id, after.Id);
        Assert.Equal(2, after.SeedVersion);
        Assert.True(after.Provisional);
        Assert.False(after.UpdateAvailable);
    }

    [Fact]
    public async Task Preset_reconciler_never_touches_edited_copies_or_other_clients()
    {
        using var catalog = new MutableCatalog();
        using var host = new GradingScaleTestHost(factory, s => catalog.Install(s));
        var a = await host.SeedAsync("GS9A");
        var b = await host.SeedAsync("GS9B", presets: false);
        var copy = await Copy(host, a, SaudiPresetSeed.SchoolKey);
        await host.SendAsync(a.SchoolAdmin, HttpMethod.Put, Routes.GradingScale(a.Tree.School1.Id, copy.Id),
            GradingScaleTestHost.Scale("Edited", bands: [new(0, 0, 100, "ALL", null, null, null, null, null)]));

        catalog.Bump();
        Assert.True(await host.ReconcileAsync(a.Tree.Org.Id) > 0);

        var templates = (await host.StoredAsync(a.Tree.Root.Id)).Where(s => s.IsTemplate).ToDictionary(s => s.SeedKey!);
        Assert.Equal(2, templates[SaudiPresetSeed.SchoolKey].SeedVersion);
        Assert.Equal(2, templates[SaudiPresetSeed.University5Key].SeedVersion);
        Assert.Equal(1, templates[SaudiPresetSeed.University4Key].SeedVersion); // its seed did not move
        var stored = Assert.Single(await host.StoredAsync(a.Tree.School1.Id));
        Assert.Equal((copy.Id, 1, "Edited"), (stored.Id, stored.SeedVersion, stored.NameEn.Replace("Scale ", string.Empty)));
        Assert.Empty(await host.StoredAsync(b.Tree.Root.Id)); // client B was not reconciled
    }

    [Fact]
    public async Task Unit_sees_ancestor_scales_but_not_sibling_scales()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS10");
        var sibling = await host.AdminAtAsync(w, w.Tree.School2);
        var own = await host.CreateScaleAsync(w.SchoolAdmin, w.Tree.School1.Id, GradingScaleTestHost.Scale("OwnS1"));
        var other = await host.CreateScaleAsync(sibling, w.Tree.School2.Id, GradingScaleTestHost.Scale("OwnS2"));

        var seen = await host.ListAsync(w.SchoolAdmin, w.Tree.School1.Id);

        Assert.Contains(seen, s => s.Id == own.Id && s.InheritedFromUnitId is null);
        Assert.DoesNotContain(seen, s => s.Id == other.Id);
        Assert.Contains(seen, s => s.IsTemplate && s.InheritedFromUnitId == w.Tree.Root.Id);
    }

    [Fact]
    public async Task Ancestor_scales_are_read_only_within_the_client()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS11");
        var rootScale = await host.CreateScaleAsync(w.RootAdmin, w.Tree.Root.Id);
        var url = Routes.GradingScale(w.Tree.School1.Id, rootScale.Id);

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Get, url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Put, url, GradingScaleTestHost.Scale("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Delete, url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.GradingScaleConfirm(w.Tree.School1.Id, rootScale.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.GradingScaleDefault(w.Tree.School1.Id, rootScale.Id))).StatusCode);
    }

    [Fact]
    public async Task Client_b_scales_never_resolve_for_client_a()
    {
        using var host = new GradingScaleTestHost(factory);
        var a = await host.SeedAsync("GS12A");
        var b = await host.SeedAsync("GS12B");
        var bScale = await host.CreateScaleAsync(b.RootAdmin, b.Tree.Root.Id);

        Assert.DoesNotContain(await host.ListAsync(a.RootAdmin, a.Tree.Root.Id), s => s.Id == bScale.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.RootAdmin, HttpMethod.Get, Routes.GradingScale(a.Tree.Root.Id, bScale.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.RootAdmin, HttpMethod.Get, Routes.OrgUnitGradingScales(b.Tree.Root.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(a.RootAdmin, HttpMethod.Post, Routes.OrgUnitGradingScales(b.Tree.Root.Id), GradingScaleTestHost.Scale())).StatusCode);
    }

    [Fact]
    public async Task Scale_in_use_cannot_be_deleted()
    {
        using var host = new GradingScaleTestHost(factory, s => s.AddScoped<IScaleUsage, AlwaysInUse>());
        var w = await host.SeedAsync("GS13");
        var scale = await host.CreateScaleAsync(w.SchoolAdmin, w.Tree.School1.Id);

        var response = await host.SendAsync(w.SchoolAdmin, HttpMethod.Delete, Routes.GradingScale(w.Tree.School1.Id, scale.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("scale_in_use", await GradingScaleTestHost.CodeAsync(response));
        Assert.True((await host.ListAsync(w.SchoolAdmin, w.Tree.School1.Id)).Single(s => s.Id == scale.Id).InUse);
    }

    [Fact]
    public async Task Teacher_can_read_but_cannot_configure_scales()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS14");
        var scale = await host.CreateScaleAsync(w.SchoolAdmin, w.Tree.School1.Id);
        var school = w.Tree.School1.Id;

        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Teacher, HttpMethod.Get, Routes.OrgUnitGradingScales(school))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.SendAsync(w.Teacher, HttpMethod.Get, Routes.GradingScale(school, scale.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Post, Routes.OrgUnitGradingScales(school), GradingScaleTestHost.Scale())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Put, Routes.GradingScale(school, scale.Id), GradingScaleTestHost.Scale())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Delete, Routes.GradingScale(school, scale.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Post, Routes.GradingScaleConfirm(school, scale.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.SendAsync(w.Teacher, HttpMethod.Post, Routes.OrgUnitGradingScalePresets(school), new CopyScalePresetRequest(SaudiPresetSeed.SchoolKey))).StatusCode);
    }

    [Fact]
    public async Task Invalid_scale_bodies_are_refused()
    {
        using var host = new GradingScaleTestHost(factory);
        var w = await host.SeedAsync("GS15");
        var url = Routes.OrgUnitGradingScales(w.Tree.School1.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, url, GradingScaleTestHost.Scale() with { NameEn = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, url, GradingScaleTestHost.Scale() with { Kind = "Stars" })).StatusCode);
        var gpaWithoutPoints = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, url, GradingScaleTestHost.Scale(kind: ScaleKinds.Gpa));
        Assert.Equal("scale_bands_invalid", await GradingScaleTestHost.CodeAsync(gpaWithoutPoints));
        var pointsOnLetter = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, url, GradingScaleTestHost.Scale(bands: GradingScaleTestHost.TwoBands(4m)));
        Assert.Equal(HttpStatusCode.BadRequest, pointsOnLetter.StatusCode);
        var unknownPreset = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitGradingScalePresets(w.Tree.School1.Id), new CopyScalePresetRequest("nope"));
        Assert.Equal(HttpStatusCode.NotFound, unknownPreset.StatusCode);
    }

    private static async Task<GradingScaleDto> Copy(GradingScaleTestHost host, GradingScaleTestHost.World w, string seedKey)
    {
        var response = await host.SendAsync(w.SchoolAdmin, HttpMethod.Post, Routes.OrgUnitGradingScalePresets(w.Tree.School1.Id), new CopyScalePresetRequest(seedKey));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<GradingScaleDto>())!;
    }

    private sealed class AlwaysInUse : IScaleUsage
    {
        public Task<bool> IsInUseAsync(Guid scaleId, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    /// <summary>A preset catalog whose seed version a test can raise: the school preset moves to version 2.</summary>
    private sealed class MutableCatalog : IScalePresetCatalog, IDisposable
    {
        private int _version = 1;

        public IReadOnlyList<SeedScale> Presets =>
            [.. SaudiPresetSeed.All.Select(p => p with { Version = p.Key == SaudiPresetSeed.SchoolKey || p.Key == SaudiPresetSeed.University5Key ? _version : 1 })];

        public void Bump() => _version = 2;

        public void Install(IServiceCollection services) => services.AddSingleton<IScalePresetCatalog>(this);

        public void Dispose()
        {
        }
    }
}
