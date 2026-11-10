using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.OrgUnits;

[Collection(ApiCollection.Name)]
public sealed class OrgUnitEndpointsTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private static CreateOrgUnitRequest Child(OrgUnit parent, LevelType level, string code, string nameEn = "Child", string nameAr = "فرع") =>
        new(parent.Id, level.Id, nameAr, nameEn, code);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(AccessTree Tree, RoleAssignment Admin, LevelType Deeper)> SetUpAsync(string name, bool adminAtRoot = true)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(
            factory, tree, adminAtRoot ? tree.Root : tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin, await _host.AddDeeperLevelAsync(tree));
    }

    [Fact]
    public async Task Admin_creates_child_and_tree_lists_it_with_depth_and_path()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU1");

        var created = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, "KG-A"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var node = (await created.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.Equal((tree.School1.Id, 2, "KG-A", "Active"), (node.ParentId, node.Depth, node.Code, node.Status));
        Assert.Equal(Routes.OrgUnit(node.Id), created.Headers.Location?.ToString());

        var tree1 = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnits, admin)).Content
            .ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;
        Assert.Equal(4, tree1.Count);
        Assert.Contains(tree1, n => n.Id == node.Id && n.Depth == 2);
        Assert.True(tree1.Single(n => n.Id == tree.School1.Id).HasChildren);
        Assert.False(tree1.Single(n => n.Id == node.Id).HasChildren);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.OrgUnits.SingleAsync(u => u.Id == node.Id);
        Assert.Equal(OrgUnitPath.Append(tree.School1.Path, node.Id), row.Path);
    }

    [Fact]
    public async Task Create_under_unit_with_CreateOrgUnits_policy_is_blocked_with_policy_message()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU2");
        await _host.AddPolicyAsync(tree, tree.School1, PolicyCatalog.CreateOrgUnits);

        var response = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, "P1"), "en");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
        Assert.Equal(PolicyCatalog.BlockedMessageEn, body.GetProperty("detail").GetString());
        Assert.Equal(tree.School1.Id, body.GetProperty("blockingOrgUnitId").GetGuid());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.OrgUnits.AnyAsync(u => u.OrganizationId == tree.Org.Id && u.Code == "P1"));
    }

    [Fact]
    public async Task School1_admin_creating_under_School2_gets_404()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU3", adminAtRoot: false);

        var response = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School2, deeper, "S2-X"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ProblemCodes.not_found, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unit_endpoints_answer_404_for_units_outside_scope_and_in_other_clients()
    {
        var (tree, admin, _) = await SetUpAsync("OU4", adminAtRoot: false);
        var other = await AccessTestData.SeedAsync(factory, "OU4b");

        foreach (var id in new[] { tree.School2.Id, other.School1.Id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnit(id), admin)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(id), admin,
                new UpdateOrgUnitRequest(tree.School1.LevelTypeId, "ا", "A", "ZZ"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitStatus(id), admin,
                new SetOrgUnitStatusRequest("Inactive"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(id), admin)).StatusCode);
        }
    }

    [Fact]
    public async Task Duplicate_code_in_client_returns_409_code_taken_but_same_code_in_other_client_is_ok()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU5", adminAtRoot: false);
        var (other, otherAdmin, otherDeeper) = await SetUpAsync("OU5b", adminAtRoot: false);

        Assert.Equal(HttpStatusCode.Created,
            (await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, "DUP"))).StatusCode);

        var sameUnit = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, "DUP"));
        Assert.Equal(HttpStatusCode.Conflict, sameUnit.StatusCode);
        Assert.Equal(ProblemCodes.code_taken, (await JsonAsync(sameUnit)).GetProperty("code").GetString());

        // the code of a unit in a subtree the caller cannot see is still taken (codes are unique per client)
        var siblingCode = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, tree.School2.Code));
        Assert.Equal(HttpStatusCode.Conflict, siblingCode.StatusCode);

        var otherClient = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, otherAdmin, Child(other.School1, otherDeeper, "DUP"));
        Assert.Equal(HttpStatusCode.Created, otherClient.StatusCode);
    }

    [Fact]
    public async Task Child_level_type_must_be_deeper_than_parent()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU6");

        var response = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            new CreateOrgUnitRequest(tree.School1.Id, tree.School1.LevelTypeId, "ب", "Same", "SAME"));
        var otherClient = await AccessTestData.SeedAsync(factory, "OU6b");
        var foreign = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            new CreateOrgUnitRequest(tree.School1.Id, otherClient.Root.LevelTypeId, "ب", "Foreign", "FOREIGN"));
        var unknown = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            new CreateOrgUnitRequest(tree.School1.Id, Guid.NewGuid(), "ب", "Unknown", "UNKNOWN"));
        var ok = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, "DEEP"));

        foreach (var bad in new[] { response, foreign, unknown })
        {
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Equal(ProblemCodes.invalid_level_type, (await JsonAsync(bad)).GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task Search_by_arabic_name_returns_match_and_ancestors()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU7");
        var created = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            Child(tree.School1, deeper, "ROSE", "Rose class", "فصل الورد"));
        var node = (await created.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;

        var byArabic = (await (await _host.SendAsync(HttpMethod.Get,
            $"{Routes.OrgUnits}?search={Uri.EscapeDataString("الورد")}", admin)).Content.ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;
        var byCode = (await (await _host.SendAsync(HttpMethod.Get,
            $"{Routes.OrgUnits}?search=rose", admin)).Content.ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;
        var none = (await (await _host.SendAsync(HttpMethod.Get,
            $"{Routes.OrgUnits}?search=zzzz-none", admin)).Content.ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;

        Assert.Equal(new[] { tree.Root.Id, tree.School1.Id, node.Id }.Order(), byArabic.Select(n => n.Id).Order());
        Assert.Equal(byArabic.Select(n => n.Id).Order(), byCode.Select(n => n.Id).Order());
        Assert.Empty(none);
    }

    [Fact]
    public async Task Node_carries_blocked_permissions_from_ancestor_policy()
    {
        var (tree, admin, _) = await SetUpAsync("OU8");
        await _host.AddPolicyAsync(tree, tree.School1, PolicyCatalog.CreateOrgUnits);

        var nodes = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnits, admin)).Content
            .ReadFromJsonAsync<List<OrgUnitNodeDto>>())!.ToDictionary(n => n.Id);
        var single = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnit(tree.School1.Id), admin)).Content
            .ReadFromJsonAsync<OrgUnitNodeDto>())!;

        Assert.Empty(nodes[tree.Root.Id].BlockedPermissions);
        Assert.Empty(nodes[tree.School2.Id].BlockedPermissions);
        Assert.Equal([Permissions.OrgUnits.Create], nodes[tree.School1.Id].BlockedPermissions);
        Assert.Equal([Permissions.OrgUnits.Create], single.BlockedPermissions);
    }

    [Fact]
    public async Task Scoped_admin_sees_only_own_subtree_in_the_tree()
    {
        var (tree, admin, _) = await SetUpAsync("OU9", adminAtRoot: false);

        var nodes = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnits, admin)).Content
            .ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;

        Assert.Equal([tree.School1.Id], nodes.Select(n => n.Id));
    }

    [Fact]
    public async Task Deactivated_unit_hides_contexts_below_and_admin_above_can_reactivate()
    {
        var (tree, admin, _) = await SetUpAsync("OU10");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, (await _host.SendAsync(HttpMethod.Get, Routes.MyPermissions, teacher)).StatusCode);

        var deactivate = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitStatus(tree.School1.Id), admin, new SetOrgUnitStatusRequest("Inactive"));

        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        Assert.Equal("Inactive", (await deactivate.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!.Status);
        var blocked = await _host.SendAsync(HttpMethod.Get, Routes.MyPermissions, teacher);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("invalid_context", (await JsonAsync(blocked)).GetProperty("code").GetString());

        var defaultTree = (await (await _host.SendAsync(HttpMethod.Get, Routes.OrgUnits, admin)).Content.ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;
        var fullTree = (await (await _host.SendAsync(HttpMethod.Get, $"{Routes.OrgUnits}?includeInactive=true", admin)).Content
            .ReadFromJsonAsync<List<OrgUnitNodeDto>>())!;
        Assert.DoesNotContain(defaultTree, n => n.Id == tree.School1.Id);
        Assert.Contains(fullTree, n => n.Id == tree.School1.Id && n.Status == "Inactive");

        var activate = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitStatus(tree.School1.Id), admin, new SetOrgUnitStatusRequest("Active"));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _host.SendAsync(HttpMethod.Get, Routes.MyPermissions, teacher)).StatusCode);
    }

    [Fact]
    public async Task Root_cannot_be_deleted_or_deactivated()
    {
        var (tree, admin, _) = await SetUpAsync("OU11");

        var deactivate = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitStatus(tree.Root.Id), admin, new SetOrgUnitStatusRequest("Inactive"));
        var delete = await _host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.Root.Id), admin);

        foreach (var response in new[] { deactivate, delete })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(ProblemCodes.root_unit, (await JsonAsync(response)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Update_changes_names_code_and_level_and_rejects_duplicate_code()
    {
        var (tree, admin, deeper) = await SetUpAsync("OU12");
        var other = (await (await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, Child(tree.School1, deeper, "UPD-A")))
            .Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;

        var ok = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(other.Id), admin,
            new UpdateOrgUnitRequest(deeper.Id, "اسم جديد", "New name", "UPD-B"));
        var sameCodeKept = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(other.Id), admin,
            new UpdateOrgUnitRequest(deeper.Id, "اسم", "Name", "UPD-B"));
        var duplicate = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(other.Id), admin,
            new UpdateOrgUnitRequest(deeper.Id, "اسم", "Name", tree.School2.Code));
        var shallow = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(other.Id), admin,
            new UpdateOrgUnitRequest(tree.School1.LevelTypeId, "اسم", "Name", "UPD-C"));

        var updated = (await ok.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        Assert.Equal(("اسم جديد", "New name", "UPD-B"), (updated.NameAr, updated.NameEn, updated.Code));
        Assert.Equal(HttpStatusCode.OK, sameCodeKept.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, shallow.StatusCode);
    }

    [Fact]
    public async Task Teacher_without_the_permission_gets_403_on_every_endpoint()
    {
        var tree = await AccessTestData.SeedAsync(factory, "OU13");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        foreach (var (method, url) in new[]
                 {
                     (HttpMethod.Get, Routes.OrgUnits), (HttpMethod.Get, Routes.OrgUnit(tree.School1.Id)),
                     (HttpMethod.Post, Routes.OrgUnits), (HttpMethod.Put, Routes.OrgUnit(tree.School1.Id)),
                     (HttpMethod.Put, Routes.OrgUnitStatus(tree.School1.Id)), (HttpMethod.Delete, Routes.OrgUnit(tree.School1.Id)),
                 })
        {
            var body = method == HttpMethod.Post || method == HttpMethod.Put ? (object)new { } : null;
            Assert.Equal(HttpStatusCode.Forbidden, (await _host.SendAsync(method, url, teacher, body)).StatusCode);
        }
    }

    [Theory]
    [InlineData("code", "bad code")]
    [InlineData("code", "")]
    [InlineData("nameAr", "  ")]
    [InlineData("nameEn", "")]
    public async Task Invalid_create_returns_validation_problem(string field, string value)
    {
        var (tree, admin, deeper) = await SetUpAsync("OU14");
        var request = Child(tree.School1, deeper, "VALID");
        request = field switch
        {
            "code" => request with { Code = value },
            "nameAr" => request with { NameAr = value },
            _ => request with { NameEn = value },
        };

        var response = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.validation_failed, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Invalid_status_value_returns_validation_problem()
    {
        var (tree, admin, _) = await SetUpAsync("OU15");

        var response = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitStatus(tree.School1.Id), admin, new SetOrgUnitStatusRequest("Gone"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_acting_on_their_own_scope_unit_still_gets_the_level_depth_rule()
    {
        var (tree, rootAdmin, deeper) = await SetUpAsync("OU16");
        var created = await _host.SendAsync(HttpMethod.Post, Routes.OrgUnits, rootAdmin, Child(tree.School1, deeper, "OWN"));
        var own = (await created.Content.ReadFromJsonAsync<OrgUnitNodeDto>())!;
        var admin = await AccessTestData.AssignAsync(factory, tree, await FindAsync(own.Id), BuiltInRoles.OrgAdmin, Guid.NewGuid());

        // the unit is the admin's scope: its parent is outside the scope but the depth rule must still apply
        var shallow = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(own.Id), admin,
            new UpdateOrgUnitRequest(tree.School1.LevelTypeId, "ا", "A", "OWN"));
        var fine = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnit(own.Id), admin,
            new UpdateOrgUnitRequest(deeper.Id, "ا", "A", "OWN"));

        Assert.Equal(HttpStatusCode.BadRequest, shallow.StatusCode);
        Assert.Equal(ProblemCodes.invalid_level_type, (await JsonAsync(shallow)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, fine.StatusCode);
    }

    private async Task<OrgUnit> FindAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.OrgUnits.SingleAsync(u => u.Id == id);
    }

    [Fact]
    public async Task Level_type_of_another_client_is_rejected_even_for_a_platform_admin()
    {
        var a = await AccessTestData.SeedAsync(factory, "OU17");
        var b = await AccessTestData.SeedAsync(factory, "OU17b");
        var foreignLevel = await _host.AddDeeperLevelAsync(b);
        var local = await _host.AddDeeperLevelAsync(a);

        var create = await _host.SendAsPlatformAdminAsync(HttpMethod.Post, Routes.OrgUnits,
            Child(a.School1, foreignLevel, "XLVL"));
        var update = await _host.SendAsPlatformAdminAsync(HttpMethod.Put, Routes.OrgUnit(a.School1.Id),
            new UpdateOrgUnitRequest(foreignLevel.Id, "ا", "A", a.School1.Code));
        var ok = await _host.SendAsPlatformAdminAsync(HttpMethod.Post, Routes.OrgUnits, Child(a.School1, local, "OKLVL"));

        foreach (var response in new[] { create, update })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.invalid_level_type, (await JsonAsync(response)).GetProperty("code").GetString());
        }

        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }
}