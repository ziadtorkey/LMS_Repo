using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.OrgUnits;

[Collection(ApiCollection.Name)]
public sealed class AcademicFlagsTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<HttpResponseMessage> PutAsync(RoleAssignment caller, OrgUnit unit, bool transfer, Guid? next, bool course = false) =>
        await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitAcademicFlags(unit.Id), caller,
            new OrgUnitAcademicFlagsRequest(transfer, next, course));

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    [Fact]
    public async Task KG_maps_to_Primary_as_next_transfer_level()
    {
        var (tree, admin) = await SetUpAsync("AF1");
        var kg = tree.School1;
        var primary = tree.School2;

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, primary, true, null)).StatusCode);
        var response = await PutAsync(admin, kg, true, primary.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = (await response.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.True(node.IsLevelTransfer);
        Assert.Equal(primary.Id, node.NextTransferUnitId);
        var got = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnit(kg.Id), admin))
            .Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.Equal(primary.Id, got.NextTransferUnitId);
    }

    [Fact]
    public async Task Next_without_flag_is_400()
    {
        var (tree, admin) = await SetUpAsync("AF2");
        await PutAsync(admin, tree.School2, true, null);

        var response = await PutAsync(admin, tree.School1, false, tree.School2.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_transfer_target, await CodeAsync(response));
    }

    [Fact]
    public async Task Target_of_other_level_type_without_flag_or_itself_is_400()
    {
        var (tree, admin) = await SetUpAsync("AF3");
        var deeperLevel = await _host.AddDeeperLevelAsync(tree);
        var deeper = (await (await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            new CreateOrgUnitRequest(tree.School1.Id, deeperLevel.Id, "أعمق", "Deeper", "D1"))).Content
            .ReadFromJsonAsync<OrgUnitNodeDto>())!;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var unit = await db.OrgUnits.SingleAsync(u => u.Id == deeper.Id);
            unit.SetAcademicFlags(true, null, false);
            await db.SaveChangesAsync();
        }

        var otherLevel = await PutAsync(admin, tree.School1, true, deeper.Id);
        Assert.Equal(HttpStatusCode.BadRequest, otherLevel.StatusCode);
        Assert.Equal(ProblemCodes.invalid_transfer_target, await CodeAsync(otherLevel));

        // School2 has the same level type but is not flagged as a transfer level.
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(admin, tree.School1, true, tree.School2.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(admin, tree.School1, true, tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Cycle_KG_Primary_KG_is_400()
    {
        var (tree, admin) = await SetUpAsync("AF4");
        await PutAsync(admin, tree.School2, true, null);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, tree.School1, true, tree.School2.Id)).StatusCode);

        var response = await PutAsync(admin, tree.School2, true, tree.School1.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_transfer_target, await CodeAsync(response));
    }

    [Fact]
    public async Task Cycle_through_a_longer_chain_is_400()
    {
        var (tree, admin) = await SetUpAsync("AF5");
        OrgUnit third;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var level = await db.LevelTypes.SingleAsync(l => l.Id == tree.Root.LevelTypeId);
            third = OrgUnit.CreateChild(tree.Root, level.Id, "ثالث", "Third", "S3");
            db.OrgUnits.Add(third);
            await db.SaveChangesAsync();
        }

        await PutAsync(admin, third, true, null);
        await PutAsync(admin, tree.School2, true, third.Id);
        await PutAsync(admin, tree.School1, true, tree.School2.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(admin, third, true, tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Clearing_flag_on_referenced_target_is_409()
    {
        var (tree, admin) = await SetUpAsync("AF6");
        await PutAsync(admin, tree.School2, true, null);
        await PutAsync(admin, tree.School1, true, tree.School2.Id);

        var response = await PutAsync(admin, tree.School2, false, null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.transfer_target_in_use, await CodeAsync(response));

        // Once nobody points at it, the flag can be cleared and the mapping goes with it.
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, tree.School1, true, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, tree.School2, false, null)).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_unit_that_is_a_transfer_target_is_409_unit_in_use()
    {
        var (tree, admin) = await SetUpAsync("AF9");
        await PutAsync(admin, tree.School2, true, null);
        await PutAsync(admin, tree.School1, true, tree.School2.Id);

        var response = await _host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.unit_in_use, await CodeAsync(response));

        // Deactivating a referenced target is allowed; only deleting is refused. Once nobody points at it, delete works.
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(admin, tree.School1, true, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await _host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin)).StatusCode);
    }

    [Fact]
    public async Task Clearing_flag_on_a_unit_also_clears_its_own_mapping_target()
    {
        var (tree, admin) = await SetUpAsync("AF7");
        await PutAsync(admin, tree.School2, true, null);
        await PutAsync(admin, tree.School1, true, tree.School2.Id);

        var response = await PutAsync(admin, tree.School1, false, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var node = (await response.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.False(node.IsLevelTransfer);
        Assert.Null(node.NextTransferUnitId);
    }

    [Fact]
    public async Task Course_management_flag_round_trips_and_is_audited()
    {
        var (tree, admin) = await SetUpAsync("AF8");

        var response = await PutAsync(admin, tree.School1, false, null, course: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!.IsCourseManagementLevel);
        var got = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnit(tree.School1.Id), admin))
            .Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.True(got.IsCourseManagementLevel);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.SingleAsync(e =>
            e.EntityId == tree.School1.Id && e.Action == AuditAction.Updated && e.UserId == admin.UserId);
        Assert.Contains("IsCourseManagementLevel", entry.AfterJson);
    }

    [Fact]
    public async Task Units_and_targets_outside_the_callers_subtree_are_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AF9");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var other = await AccessTestData.SeedAsync(factory, "AF9B");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.OrgUnits.SingleAsync(u => u.Id == other.School1.Id)).SetAcademicFlags(true, null, false);
            (await db.OrgUnits.SingleAsync(u => u.Id == tree.School2.Id)).SetAcademicFlags(true, null, false);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(schoolAdmin, tree.School2, true, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(schoolAdmin, tree.School1, true, tree.School2.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(schoolAdmin, tree.School1, true, other.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_set_flags()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AF10");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(teacher, tree.School1, true, null)).StatusCode);
    }
}
