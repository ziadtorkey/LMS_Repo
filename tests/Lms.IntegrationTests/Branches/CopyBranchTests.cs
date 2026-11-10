using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Configuration;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Branches;

[Collection(ApiCollection.Name)]
public sealed class CopyBranchTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    /// <summary>Root → School1 → X (level 2) → Y, Z (level 3); School2 is the copy target.</summary>
    private sealed record Fixture(AccessTree Tree, RoleAssignment Admin, OrgUnit X, OrgUnit Y, OrgUnit Z);

    private async Task<Fixture> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var level2 = new LevelType(tree.Org.Id, "ثانٍ", "Second", 2, true);
        var level3 = new LevelType(tree.Org.Id, "ثالث", "Third", 3, true);
        var x = OrgUnit.CreateChild(tree.School1, level2.Id, "س", "X", "X");
        var y = OrgUnit.CreateChild(x, level3.Id, "ص", "Y", "Y");
        var z = OrgUnit.CreateChild(x, level3.Id, "ع", "Z", "Z");
        db.AddRange(level2, level3, x, y, z);
        await db.SaveChangesAsync();
        return new Fixture(tree, admin, x, y, z);
    }

    private Task<HttpResponseMessage> CopyAsync(
        RoleAssignment caller, OrgUnit source, OrgUnit target, bool policies = false, bool settings = false,
        string suffix = "-C", string? nameEn = null) =>
        _host.SendAsync(HttpMethod.Post, Routes.OrgUnitsCopy, caller,
            new CopyBranchRequest(source.Id, target.Id, suffix, policies, settings, null, nameEn));

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<List<OrgUnit>> UnitsAsync(Guid orgId, string codeLike)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.OrgUnits.Where(u => u.OrganizationId == orgId && u.Code.StartsWith(codeLike)).OrderBy(u => u.Depth).ThenBy(u => u.Code).ToListAsync();
    }

    [Fact]
    public async Task Copy_structure_only_creates_new_units_with_suffixed_codes()
    {
        var f = await SetUpAsync("CB1");
        await _host.AddPolicyAsync(f.Tree, f.X, PolicyCatalog.CreateOrgUnits);

        var response = await CopyAsync(f.Admin, f.X, f.Tree.School2, nameEn: "X copy");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CopyBranchResultDto>())!;
        Assert.Equal(3, result.UnitsCreated);
        var copies = (await UnitsAsync(f.Tree.Org.Id, "X-C")).Concat(await UnitsAsync(f.Tree.Org.Id, "Y-C"))
            .Concat(await UnitsAsync(f.Tree.Org.Id, "Z-C")).ToList();
        Assert.Equal(3, copies.Count);
        var root = copies.Single(u => u.Code == "X-C");
        Assert.Equal(result.NewRootId, root.Id);
        Assert.Equal(f.Tree.School2.Id, root.ParentId);
        Assert.Equal("X copy", root.NameEn);
        Assert.All(copies.Where(u => u.Code != "X-C"), u => Assert.Equal(root.Id, u.ParentId));
        Assert.All(copies, u => Assert.StartsWith(f.Tree.School2.Path, u.Path));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await db.Set<OrgUnitPolicy>().Where(p => copies.Select(c => c.Id).Contains(p.OrgUnitId)).ToListAsync());
    }

    [Fact]
    public async Task Copy_with_policies_and_settings_copies_rows_to_new_units()
    {
        var f = await SetUpAsync("CB2");
        await _host.AddPolicyAsync(f.Tree, f.X, PolicyCatalog.CreateOrgUnits);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitSetting(f.Tree.Org.Id, f.Y.Id, f.Y.Path, SettingKeys.FilesMaxUploadMb.Name, "50", false));
            await db.SaveChangesAsync();
        }

        var response = await CopyAsync(f.Admin, f.X, f.Tree.School2, policies: true, settings: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<CopyBranchResultDto>())!.SkippedLockedSettings);
        var xCopy = (await UnitsAsync(f.Tree.Org.Id, "X-C")).Single();
        var yCopy = (await UnitsAsync(f.Tree.Org.Id, "Y-C")).Single();
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var policy = await read.Set<OrgUnitPolicy>().SingleAsync(p => p.OrgUnitId == xCopy.Id);
        Assert.Equal(PolicyCatalog.CreateOrgUnits, policy.PolicyKey);
        Assert.Equal(xCopy.Path, policy.OrgUnitPath);
        var setting = await read.Set<OrgUnitSetting>().SingleAsync(s => s.OrgUnitId == yCopy.Id);
        Assert.Equal("50", setting.ValueJson);
        Assert.Equal(yCopy.Path, setting.OrgUnitPath);
    }

    [Fact]
    public async Task Transfer_mapping_inside_branch_is_remapped()
    {
        var f = await SetUpAsync("CB3");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.OrgUnits.SingleAsync(u => u.Id == f.Z.Id)).SetAcademicFlags(true, null, true);
            (await db.OrgUnits.SingleAsync(u => u.Id == f.Y.Id)).SetAcademicFlags(true, f.Z.Id, false);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Created, (await CopyAsync(f.Admin, f.X, f.Tree.School2)).StatusCode);

        var yCopy = (await UnitsAsync(f.Tree.Org.Id, "Y-C")).Single();
        var zCopy = (await UnitsAsync(f.Tree.Org.Id, "Z-C")).Single();
        Assert.True(yCopy.IsLevelTransfer);
        Assert.Equal(zCopy.Id, yCopy.NextTransferUnitId);
        Assert.NotEqual(f.Z.Id, yCopy.NextTransferUnitId);
        Assert.True(zCopy.IsCourseManagementLevel);
    }

    [Fact]
    public async Task Setting_locked_above_target_is_skipped_and_reported()
    {
        var f = await SetUpAsync("CB4");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new OrgUnitSetting(f.Tree.Org.Id, f.Y.Id, f.Y.Path, SettingKeys.FilesMaxUploadMb.Name, "50", false));
            db.Add(new OrgUnitSetting(f.Tree.Org.Id, f.Y.Id, f.Y.Path, SettingKeys.UiShowHijriDates.Name, "true", false));
            db.Add(new OrgUnitSetting(f.Tree.Org.Id, f.Tree.School2.Id, f.Tree.School2.Path, SettingKeys.FilesMaxUploadMb.Name, "10", true));
            await db.SaveChangesAsync();
        }

        var response = await CopyAsync(f.Admin, f.X, f.Tree.School2, settings: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([SettingKeys.FilesMaxUploadMb.Name], (await response.Content.ReadFromJsonAsync<CopyBranchResultDto>())!.SkippedLockedSettings);
        var yCopy = (await UnitsAsync(f.Tree.Org.Id, "Y-C")).Single();
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var copied = await read.Set<OrgUnitSetting>().Where(s => s.OrgUnitId == yCopy.Id).Select(s => s.Key).ToListAsync();
        Assert.Equal([SettingKeys.UiShowHijriDates.Name], copied);
    }

    [Fact]
    public async Task Target_with_CreateOrgUnits_policy_is_blocked()
    {
        var f = await SetUpAsync("CB5");
        await _host.AddPolicyAsync(f.Tree, f.Tree.School2, PolicyCatalog.CreateOrgUnits);

        var response = await CopyAsync(f.Admin, f.X, f.Tree.School2);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("BlockedByPolicy", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());
        Assert.Empty(await UnitsAsync(f.Tree.Org.Id, "X-C"));
    }

    [Fact]
    public async Task Source_in_other_subtree_is_404()
    {
        var f = await SetUpAsync("CB6");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.School2, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await CopyAsync(schoolAdmin, f.X, f.Tree.School2);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await UnitsAsync(f.Tree.Org.Id, "X-C"));
    }

    [Fact]
    public async Task Copy_into_own_branch_is_409()
    {
        var f = await SetUpAsync("CB7");

        var response = await CopyAsync(f.Admin, f.X, f.Y);
        var self = await CopyAsync(f.Admin, f.X, f.X);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_move, await CodeAsync(response));
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
    }

    [Fact]
    public async Task Code_conflict_returns_409_and_creates_nothing()
    {
        var f = await SetUpAsync("CB8");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(OrgUnit.CreateChild(f.Tree.School2, f.Y.LevelTypeId, "ص", "Taken", "Y-C"));
            await db.SaveChangesAsync();
        }

        var response = await CopyAsync(f.Admin, f.X, f.Tree.School2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.code_taken, await CodeAsync(response));
        Assert.Contains("Y-C", (await response.Content.ReadAsStringAsync()));
        Assert.Empty(await UnitsAsync(f.Tree.Org.Id, "X-C"));
        Assert.Empty(await UnitsAsync(f.Tree.Org.Id, "Z-C"));
    }

    [Fact]
    public async Task Invalid_suffix_and_unfitting_levels_are_400()
    {
        var f = await SetUpAsync("CB9");

        Assert.Equal(HttpStatusCode.BadRequest, (await CopyAsync(f.Admin, f.X, f.Tree.School2, suffix: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CopyAsync(f.Admin, f.X, f.Tree.School2, suffix: "a b")).StatusCode);
        // A level 2 branch cannot go below a level 3 unit's sibling at the same depth: Y (level 3) is not shallower than X (level 2).
        Assert.Equal(HttpStatusCode.BadRequest, (await CopyAsync(f.Admin, f.Tree.School2, f.Y)).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_copy()
    {
        var f = await SetUpAsync("CB10");
        var teacher = await AccessTestData.AssignAsync(factory, f.Tree, f.Tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await CopyAsync(teacher, f.X, f.Tree.School2)).StatusCode);
    }
}
