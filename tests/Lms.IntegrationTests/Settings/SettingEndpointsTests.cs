using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Api.Features.Files;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Settings;

[Collection(ApiCollection.Name)]
public sealed class SettingEndpointsTests(LmsApiFactory factory)
{
    private static readonly string Hijri = SettingKeys.UiShowHijriDates.Name;
    private static readonly string Culture = SettingKeys.UiDefaultCulture.Name;
    private static readonly string MaxMb = SettingKeys.FilesMaxUploadMb.Name;
    private static readonly string VirtualClasses = FeatureFlags.VirtualClasses.Name;

    private HttpClient Client => factory.CreateClient();

    private static HttpRequestMessage Request(HttpMethod method, string url, RoleAssignment? who, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (who is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<List<EffectiveSettingDto>> EffectiveListAsync(RoleAssignment who, Guid unitId)
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitSettings(unitId), who));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<EffectiveSettingDto>>())!;
    }

    private async Task<EffectiveSettingDto> EffectiveAsync(RoleAssignment who, Guid unitId, string name) =>
        (await EffectiveListAsync(who, unitId)).Single(x => x.Name == name);

    private Task<HttpResponseMessage> SetAsync(RoleAssignment who, Guid unitId, string name, string valueJson, bool locked = false) =>
        Client.SendAsync(Request(HttpMethod.Put, Routes.OrgUnitSetting(unitId, name), who, new SetSettingRequest(valueJson, locked)));

    private Task<HttpResponseMessage> RemoveAsync(RoleAssignment who, Guid unitId, string name) =>
        Client.SendAsync(Request(HttpMethod.Delete, Routes.OrgUnitSetting(unitId, name), who));

    private async Task<OrgUnit> AddChildAsync(AccessTree tree, OrgUnit parent, string code)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var levelId = db.LevelTypes.Single(x => x.OrganizationId == tree.Org.Id).Id;
        var unit = OrgUnit.CreateChild(parent, levelId, "وحدة " + code, "Unit " + code, code);
        db.Add(unit);
        await db.SaveChangesAsync();
        return unit;
    }

    private async Task<(AccessTree Tree, RoleAssignment RootAdmin)> RootAdminAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        return (tree, await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    [Fact]
    public async Task Catalog_lists_the_settings_with_their_rules()
    {
        var (_, admin) = await RootAdminAsync("SE0");

        var response = await Client.SendAsync(Request(HttpMethod.Get, Routes.SettingsCatalog, admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = (await response.Content.ReadFromJsonAsync<List<SettingDefinition>>())!;
        Assert.Equal(SettingCatalog.All.Select(x => x.Name), catalog.Select(x => x.Name));
        Assert.Equal(["ar", "en"], catalog.Single(x => x.Name == Culture).AllowedValues);
    }

    [Fact]
    public async Task Effective_view_lists_every_catalog_key_with_default_source_null()
    {
        var (tree, admin) = await RootAdminAsync("SE1");

        var all = await EffectiveListAsync(admin, tree.School1.Id);

        Assert.Equal(SettingCatalog.All.Select(x => x.Name), all.Select(x => x.Name));
        foreach (var setting in all)
        {
            Assert.Equal(SettingCatalog.Find(setting.Name)!.DefaultJson, setting.ValueJson);
            Assert.Null(setting.SourceOrgUnitId);
            Assert.Null(setting.SourceNameEn);
            Assert.False(setting.DefinedHere);
            Assert.False(setting.IsLocked);
            Assert.False(setting.LockedByAncestor);
        }
    }

    [Fact]
    public async Task Value_at_root_is_inherited_by_school_with_source()
    {
        var (tree, admin) = await RootAdminAsync("SE2");

        var put = await SetAsync(admin, tree.Root.Id, Culture, "\"en\"");
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var atRoot = (await put.Content.ReadFromJsonAsync<EffectiveSettingDto>())!;
        Assert.True(atRoot.DefinedHere);
        Assert.Equal("\"en\"", atRoot.ValueJson);

        var atSchool = await EffectiveAsync(admin, tree.School1.Id, Culture);
        Assert.Equal("\"en\"", atSchool.ValueJson);
        Assert.False(atSchool.DefinedHere);
        Assert.Equal(tree.Root.Id, atSchool.SourceOrgUnitId);
        Assert.Equal(tree.Root.NameEn, atSchool.SourceNameEn);
        Assert.Equal(tree.Root.NameAr, atSchool.SourceNameAr);

        // A school override wins below it; the sibling still inherits the root value.
        await SetAsync(admin, tree.School1.Id, Culture, "\"ar\"");
        var overridden = await EffectiveAsync(admin, tree.School1.Id, Culture);
        Assert.Equal("\"ar\"", overridden.ValueJson);
        Assert.True(overridden.DefinedHere);
        Assert.Equal(tree.School1.Id, overridden.SourceOrgUnitId);
        Assert.Equal("\"en\"", (await EffectiveAsync(admin, tree.School2.Id, Culture)).ValueJson);
    }

    [Fact]
    public async Task Locked_root_value_blocks_school_override_409_setting_locked()
    {
        var (tree, admin) = await RootAdminAsync("SE3");
        await SetAsync(admin, tree.Root.Id, MaxMb, "50", locked: true);

        var atSchool = await EffectiveAsync(admin, tree.School1.Id, MaxMb);
        Assert.Equal("50", atSchool.ValueJson);
        Assert.True(atSchool.IsLocked);
        Assert.True(atSchool.LockedByAncestor);

        var response = await SetAsync(admin, tree.School1.Id, MaxMb, "10");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ProblemCodes.setting_locked, problem.GetProperty("code").GetString());
        Assert.Equal(tree.Root.Id, problem.GetProperty("extra").GetProperty("lockingOrgUnitId").GetGuid());
        Assert.Equal("50", (await EffectiveAsync(admin, tree.School1.Id, MaxMb)).ValueJson);

        // The unit that holds the lock can still change its own value.
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(admin, tree.Root.Id, MaxMb, "60", locked: true)).StatusCode);
        Assert.Equal("60", (await EffectiveAsync(admin, tree.School1.Id, MaxMb)).ValueJson);
    }

    [Fact]
    public async Task Feature_flag_enabled_at_school_resolves_true_for_its_sections_only()
    {
        var (tree, admin) = await RootAdminAsync("SE4");
        var sectionOfSchool1 = await AddChildAsync(tree, tree.School1, "SEC1");
        var sectionOfSchool2 = await AddChildAsync(tree, tree.School2, "SEC2");

        Assert.Equal(HttpStatusCode.OK, (await SetAsync(admin, tree.School1.Id, VirtualClasses, "true")).StatusCode);

        Assert.Equal("true", (await EffectiveAsync(admin, sectionOfSchool1.Id, VirtualClasses)).ValueJson);
        Assert.Equal(tree.School1.Id, (await EffectiveAsync(admin, sectionOfSchool1.Id, VirtualClasses)).SourceOrgUnitId);
        Assert.Equal("false", (await EffectiveAsync(admin, sectionOfSchool2.Id, VirtualClasses)).ValueJson);
        Assert.Equal("false", (await EffectiveAsync(admin, tree.Root.Id, VirtualClasses)).ValueJson);

        // The typed resolver the rest of the system uses agrees.
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resolver = new SettingsResolver(db, TestCurrentContext.PlatformAdmin());
        Assert.True((await resolver.ResolveAsync(FeatureFlags.VirtualClasses, sectionOfSchool1.Path)).Value);
        Assert.False((await resolver.ResolveAsync(FeatureFlags.VirtualClasses, sectionOfSchool2.Path)).Value);
    }

    [Fact]
    public async Task Remove_override_falls_back_to_inherited()
    {
        var (tree, admin) = await RootAdminAsync("SE5");
        await SetAsync(admin, tree.Root.Id, Culture, "\"en\"");
        await SetAsync(admin, tree.School1.Id, Culture, "\"ar\"");
        Assert.Equal("\"ar\"", (await EffectiveAsync(admin, tree.School1.Id, Culture)).ValueJson);

        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync(admin, tree.School1.Id, Culture)).StatusCode);

        var after = await EffectiveAsync(admin, tree.School1.Id, Culture);
        Assert.Equal("\"en\"", after.ValueJson);
        Assert.False(after.DefinedHere);
        Assert.Equal(tree.Root.Id, after.SourceOrgUnitId);

        // Nothing left to remove here; and the value can be set again afterwards.
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(admin, tree.School1.Id, Culture)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(admin, tree.School1.Id, Culture, "\"ar\"")).StatusCode);
    }

    [Fact]
    public async Task Removing_the_root_value_falls_back_to_the_catalog_default()
    {
        var (tree, admin) = await RootAdminAsync("SE6");
        await SetAsync(admin, tree.Root.Id, Hijri, "true");

        await RemoveAsync(admin, tree.Root.Id, Hijri);

        var after = await EffectiveAsync(admin, tree.School1.Id, Hijri);
        Assert.Equal("false", after.ValueJson);
        Assert.Null(after.SourceOrgUnitId);
    }

    [Fact]
    public async Task Invalid_values_and_unknown_names_are_refused()
    {
        var (tree, admin) = await RootAdminAsync("SE7");

        foreach (var (name, value) in new[]
                 {
                     (MaxMb, "0"), (MaxMb, "2049"), (MaxMb, "\"200\""), (Culture, "\"fr\""), (Culture, "5"),
                     (Hijri, "1"), (Hijri, "\"true\""), (Hijri, "not json"), (Hijri, ""),
                 })
        {
            var response = await SetAsync(admin, tree.School1.Id, name, value);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(ProblemCodes.validation_failed, problem.GetProperty("code").GetString());
            Assert.True(problem.TryGetProperty("errors", out _));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(admin, tree.School1.Id, "No.SuchSetting", "1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(admin, tree.School1.Id, "No.SuchSetting")).StatusCode);

        // Nothing was stored by the refused requests.
        Assert.All(await EffectiveListAsync(admin, tree.School1.Id), s => Assert.False(s.DefinedHere));
    }

    [Fact]
    public async Task Upload_limit_is_bounded_by_the_files_transport_ceiling()
    {
        // Files.MaxUploadMb may never promise more than the upload endpoint can transport (QA 2 on #35).
        var max = SettingCatalog.Find(SettingKeys.FilesMaxUploadMb.Name)!.Max!.Value;
        Assert.True(FileEndpoints.TransportLimitBytes >= max * 1024L * 1024L,
            $"Catalog max {max} MB exceeds the transport ceiling of {FileEndpoints.TransportLimitBytes} bytes.");

        var (tree, admin) = await RootAdminAsync("SE8");
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(admin, tree.Root.Id, MaxMb, max.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetAsync(admin, tree.Root.Id, MaxMb, (max + 1).ToString())).StatusCode);
    }

    [Fact]
    public async Task Manager_can_view_not_update()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SE9");
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Equal(SettingCatalog.All.Count, (await EffectiveListAsync(manager, tree.School1.Id)).Count);
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(Request(HttpMethod.Get, Routes.SettingsCatalog, manager))).StatusCode);

        var put = await SetAsync(manager, tree.School1.Id, Hijri, "true");
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("MissingPermission", (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await RemoveAsync(manager, tree.School1.Id, Hijri)).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_read_settings_and_anonymous_gets_401()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SE10");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitSettings(tree.School1.Id), teacher))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.SendAsync(Request(HttpMethod.Get, Routes.SettingsCatalog, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetAsync(null!, tree.School1.Id, Hijri, "true")).StatusCode);
    }

    [Fact]
    public async Task Other_subtree_unit_returns_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "SE11");
        var other = await AccessTestData.SeedAsync(factory, "SE11B");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        foreach (var unitId in new[] { tree.School2.Id, tree.Root.Id, other.School1.Id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitSettings(unitId), schoolAdmin))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SetAsync(schoolAdmin, unitId, Hijri, "true")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await RemoveAsync(schoolAdmin, unitId, Hijri)).StatusCode);
        }

        // Inside its own subtree it can set values, and sees what the root above it imposes.
        Assert.Equal(HttpStatusCode.OK, (await SetAsync(schoolAdmin, tree.School1.Id, Hijri, "true")).StatusCode);
    }

    [Fact]
    public async Task School_admin_sees_a_lock_set_at_the_root_and_cannot_change_it()
    {
        var (tree, rootAdmin) = await RootAdminAsync("SE12");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await SetAsync(rootAdmin, tree.Root.Id, Culture, "\"en\"", locked: true);

        var seen = await EffectiveAsync(schoolAdmin, tree.School1.Id, Culture);

        Assert.True(seen.LockedByAncestor);
        Assert.Equal(tree.Root.Id, seen.SourceOrgUnitId);
        Assert.Equal(HttpStatusCode.Conflict, (await SetAsync(schoolAdmin, tree.School1.Id, Culture, "\"ar\"")).StatusCode);
    }

    [Fact]
    public async Task Setting_change_is_audited()
    {
        var (tree, admin) = await RootAdminAsync("SE13");
        await SetAsync(admin, tree.School1.Id, Hijri, "true");
        await SetAsync(admin, tree.School1.Id, Hijri, "false");
        await RemoveAsync(admin, tree.School1.Id, Hijri);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var rowId = (await db.Set<OrgUnitSetting>().IgnoreQueryFilters().SingleAsync(x => x.OrgUnitId == tree.School1.Id)).Id;
        var entries = await db.AuditEntries.Where(x => x.EntityId == rowId).OrderBy(x => x.TimestampUtc).ToListAsync();

        Assert.Equal([AuditAction.Created, AuditAction.Updated, AuditAction.Deleted], entries.Select(x => x.Action));
        Assert.All(entries, e => Assert.Equal(admin.UserId, e.UserId));
        Assert.Contains("true", entries[1].BeforeJson);
        Assert.Contains("false", entries[1].AfterJson);
    }

    [Fact]
    public async Task Arabic_string_values_round_trip_through_the_untyped_path()
    {
        // The untyped JSON path must keep non-ASCII text exactly (the catalog has no free-text setting yet, so this is
        // exercised through the resolver the endpoints use).
        var (tree, _) = await RootAdminAsync("SE14");
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var resolver = new SettingsResolver(db, TestCurrentContext.PlatformAdmin());

        await resolver.SetJsonAsync("X.Free.Text", tree.Root.Id, "\"مدرسة النور 🌟\"", isLocked: false);

        var row = await db.Set<OrgUnitSetting>().SingleAsync(x => x.OrgUnitId == tree.Root.Id && x.Key == "X.Free.Text");
        Assert.Equal("\"مدرسة النور 🌟\"", row.ValueJson);
    }
}
