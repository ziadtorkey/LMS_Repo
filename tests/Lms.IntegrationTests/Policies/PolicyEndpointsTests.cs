using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Auditing;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Policies;

[Collection(ApiCollection.Name)]
public sealed class PolicyEndpointsTests(LmsApiFactory factory)
{
    private const string Key = PolicyCatalog.CreateOrgUnits;

    private HttpClient Client => factory.CreateClient();

    private static HttpRequestMessage Request(HttpMethod method, string url, RoleAssignment? who)
    {
        var request = new HttpRequestMessage(method, url);
        if (who is not null)
        {
            request.Headers.Add(TestAuthHandler.UserHeader, who.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, who.Id.ToString());
        }

        return request;
    }

    private async Task<EffectivePolicyDto> EffectiveAsync(RoleAssignment who, Guid unitId, string key = Key)
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitPolicies(unitId), who));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var all = (await response.Content.ReadFromJsonAsync<List<EffectivePolicyDto>>())!;
        return all.Single(x => x.Key == key);
    }

    private async Task<HttpResponseMessage> EnableAsync(RoleAssignment who, Guid unitId, string key = Key) =>
        await Client.SendAsync(Request(HttpMethod.Put, Routes.OrgUnitPolicy(unitId, key), who));

    private async Task<HttpResponseMessage> DisableAsync(RoleAssignment who, Guid unitId, string key = Key) =>
        await Client.SendAsync(Request(HttpMethod.Delete, Routes.OrgUnitPolicy(unitId, key), who));

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
    public async Task Catalog_lists_every_policy_with_the_permissions_it_blocks()
    {
        var (_, admin) = await RootAdminAsync("PO0");

        var response = await Client.SendAsync(Request(HttpMethod.Get, Routes.PoliciesCatalog, admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = (await response.Content.ReadFromJsonAsync<List<PolicyDefinitionDto>>())!;
        Assert.Equal(PolicyCatalog.BlockedPermissions.Keys.Order(), catalog.Select(x => x.Key).Order());
        Assert.Equal([Permissions.OrgUnits.Create], catalog.Single(x => x.Key == Key).BlockedPermissions);
    }

    [Fact]
    public async Task Effective_view_shows_inherited_policy_with_source_unit()
    {
        var (tree, admin) = await RootAdminAsync("PO1");
        var section = await AddChildAsync(tree, tree.School1, "SEC");
        Assert.Equal(PolicyStates.Off, (await EffectiveAsync(admin, section.Id)).State);

        // Set at School1: the section below inherits it, naming School1 as the source.
        Assert.Equal(HttpStatusCode.OK, (await EnableAsync(admin, tree.School1.Id)).StatusCode);
        var atSchool = await EffectiveAsync(admin, tree.School1.Id);
        Assert.Equal(PolicyStates.SetHere, atSchool.State);
        Assert.Equal(tree.School1.Id, atSchool.SourceOrgUnitId);
        var inherited = await EffectiveAsync(admin, section.Id);
        Assert.Equal(PolicyStates.Inherited, inherited.State);
        Assert.Equal(tree.School1.Id, inherited.SourceOrgUnitId);
        Assert.Equal(tree.School1.NameEn, inherited.SourceNameEn);
        Assert.Equal(tree.School1.NameAr, inherited.SourceNameAr);

        // The sibling and the root are unaffected.
        Assert.Equal(PolicyStates.Off, (await EffectiveAsync(admin, tree.School2.Id)).State);
        Assert.Equal(PolicyStates.Off, (await EffectiveAsync(admin, tree.Root.Id)).State);

        // Set higher up as well: the highest holder is the source.
        await EnableAsync(admin, tree.Root.Id);
        var viaRoot = await EffectiveAsync(admin, section.Id);
        Assert.Equal(PolicyStates.Inherited, viaRoot.State);
        Assert.Equal(tree.Root.Id, viaRoot.SourceOrgUnitId);
        Assert.Equal(PolicyStates.SetHere, (await EffectiveAsync(admin, tree.School1.Id)).State);
    }

    [Fact]
    public async Task Effective_view_lists_every_catalog_key()
    {
        var (tree, admin) = await RootAdminAsync("PO2");

        var response = await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitPolicies(tree.Root.Id), admin));
        var all = (await response.Content.ReadFromJsonAsync<List<EffectivePolicyDto>>())!;

        Assert.Equal(PolicyCatalog.BlockedPermissions.Keys.Order(), all.Select(x => x.Key).Order());
    }

    [Fact]
    public async Task Enable_at_child_then_disable_works()
    {
        var (tree, admin) = await RootAdminAsync("PO3");

        Assert.Equal(HttpStatusCode.OK, (await EnableAsync(admin, tree.School1.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await EnableAsync(admin, tree.School1.Id)).StatusCode); // idempotent
        Assert.Equal(PolicyStates.SetHere, (await EffectiveAsync(admin, tree.School1.Id)).State);

        Assert.Equal(HttpStatusCode.NoContent, (await DisableAsync(admin, tree.School1.Id)).StatusCode);
        Assert.Equal(PolicyStates.Off, (await EffectiveAsync(admin, tree.School1.Id)).State);

        // And on again after being off.
        Assert.Equal(HttpStatusCode.OK, (await EnableAsync(admin, tree.School1.Id)).StatusCode);
        Assert.Equal(PolicyStates.SetHere, (await EffectiveAsync(admin, tree.School1.Id)).State);
    }

    [Fact]
    public async Task Disable_inherited_policy_returns_409_policy_inherited()
    {
        var (tree, admin) = await RootAdminAsync("PO4");
        await EnableAsync(admin, tree.Root.Id);

        var response = await DisableAsync(admin, tree.School1.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.policy_inherited, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(PolicyStates.Inherited, (await EffectiveAsync(admin, tree.School1.Id)).State);

        // Only the unit that holds it can remove it.
        Assert.Equal(HttpStatusCode.NoContent, (await DisableAsync(admin, tree.Root.Id)).StatusCode);
        Assert.Equal(PolicyStates.Off, (await EffectiveAsync(admin, tree.School1.Id)).State);
    }

    [Fact]
    public async Task Disabling_a_policy_that_is_off_and_unknown_keys_are_404()
    {
        var (tree, admin) = await RootAdminAsync("PO5");

        Assert.Equal(HttpStatusCode.NotFound, (await DisableAsync(admin, tree.School1.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnableAsync(admin, tree.School1.Id, "NoSuchPolicy")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DisableAsync(admin, tree.School1.Id, "NoSuchPolicy")).StatusCode);
    }

    [Fact]
    public async Task Manager_can_view_but_not_update_403()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PO6");
        var manager = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.Equal(PolicyStates.Off, (await EffectiveAsync(manager, tree.School1.Id)).State);
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(Request(HttpMethod.Get, Routes.PoliciesCatalog, manager))).StatusCode);

        var put = await EnableAsync(manager, tree.School1.Id);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("MissingPermission", (await put.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await DisableAsync(manager, tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_read_policies_and_anonymous_gets_401()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PO7");
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitPolicies(tree.School1.Id), teacher))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.SendAsync(Request(HttpMethod.Get, Routes.PoliciesCatalog, teacher))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.SendAsync(Request(HttpMethod.Get, Routes.PoliciesCatalog, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await EnableAsync(null!, tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Other_subtree_unit_returns_404()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PO8");
        var other = await AccessTestData.SeedAsync(factory, "PO8B");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        // A sibling unit, the unit above, and another client's unit: all invisible, never 403.
        foreach (var unitId in new[] { tree.School2.Id, tree.Root.Id, other.School1.Id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound,
                (await Client.SendAsync(Request(HttpMethod.Get, Routes.OrgUnitPolicies(unitId), schoolAdmin))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await EnableAsync(schoolAdmin, unitId)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await DisableAsync(schoolAdmin, unitId)).StatusCode);
        }

        // Inside its own subtree it can enable, and the policy stays at that unit.
        Assert.Equal(HttpStatusCode.OK, (await EnableAsync(schoolAdmin, tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Sub_unit_admin_sees_a_policy_inherited_from_above_with_its_source()
    {
        var (tree, rootAdmin) = await RootAdminAsync("PO9");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await EnableAsync(rootAdmin, tree.Root.Id);

        var state = await EffectiveAsync(schoolAdmin, tree.School1.Id);

        // The root itself is above the scope of this admin, but the inherited rule and where it comes from are shown.
        Assert.Equal(PolicyStates.Inherited, state.State);
        Assert.Equal(tree.Root.Id, state.SourceOrgUnitId);
        Assert.Equal(HttpStatusCode.Conflict, (await DisableAsync(schoolAdmin, tree.School1.Id)).StatusCode);
    }

    [Fact]
    public async Task Policy_change_notifies_managers_below()
    {
        var (tree, admin) = await RootAdminAsync("PO10");
        var section = await AddChildAsync(tree, tree.School1, "SEC");
        var managerAtSchool = Guid.NewGuid();
        var managerBelow = Guid.NewGuid();
        var managerSibling = Guid.NewGuid();
        var managerAbove = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, managerAtSchool);
        await AccessTestData.AssignAsync(factory, tree, section, BuiltInRoles.Manager, managerBelow);
        await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Manager, managerSibling);
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, managerAbove);

        await EnableAsync(admin, tree.School1.Id);

        async Task<List<Notification>> RowsAsync(Guid user)
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            return await db.Set<Notification>().Where(x => x.UserId == user).ToListAsync();
        }

        var atSchool = Assert.Single(await RowsAsync(managerAtSchool));
        Assert.Equal(NotificationEvents.OrganizationPolicyChanged, atSchool.EventKey);
        var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(atSchool.ParametersJson)!;
        Assert.Equal(Key, parameters["PolicyKey"]);
        Assert.Equal("On", parameters["State"]);
        Assert.Equal(tree.School1.NameEn, parameters["OrgUnitNameEn"]);
        Assert.Equal(tree.School1.NameAr, parameters["OrgUnitNameAr"]);
        Assert.Equal(tree.Org.Id, atSchool.OrganizationId);
        Assert.Single(await RowsAsync(managerBelow));
        Assert.Empty(await RowsAsync(managerSibling));
        Assert.Empty(await RowsAsync(managerAbove));
        Assert.Empty(await RowsAsync(admin.UserId)); // the actor is never notified

        // Turning it off notifies again; a repeated enable (no change) does not.
        await EnableAsync(admin, tree.School1.Id);
        Assert.Single(await RowsAsync(managerAtSchool));
        await DisableAsync(admin, tree.School1.Id);
        var rows = await RowsAsync(managerAtSchool);
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => JsonSerializer.Deserialize<Dictionary<string, string>>(r.ParametersJson)!["State"] == "Off");
    }

    [Fact]
    public async Task Rejected_change_does_not_notify()
    {
        var (tree, admin) = await RootAdminAsync("PO11");
        var manager = Guid.NewGuid();
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, manager);
        await EnableAsync(admin, tree.Root.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.RemoveRange(await db.Set<Notification>().Where(x => x.UserId == manager).ToListAsync());
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict, (await DisableAsync(admin, tree.School1.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EnableAsync(admin, tree.School1.Id, "NoSuchPolicy")).StatusCode);

        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await check.Set<Notification>().Where(x => x.UserId == manager).ToListAsync());
    }

    [Fact]
    public async Task Policy_toggle_is_audited()
    {
        var (tree, admin) = await RootAdminAsync("PO12");
        await EnableAsync(admin, tree.School1.Id);
        await DisableAsync(admin, tree.School1.Id);
        await EnableAsync(admin, tree.School1.Id);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var policyId = (await db.Set<Lms.Domain.Configuration.OrgUnitPolicy>().IgnoreQueryFilters()
            .SingleAsync(x => x.OrgUnitId == tree.School1.Id)).Id;
        var entries = await db.AuditEntries.Where(x => x.EntityId == policyId).OrderBy(x => x.TimestampUtc).ToListAsync();

        Assert.Equal([AuditAction.Created, AuditAction.Deleted, AuditAction.Updated], entries.Select(x => x.Action));
        Assert.All(entries, e => Assert.Equal(admin.UserId, e.UserId));
        Assert.All(entries, e => Assert.Equal(tree.Org.Id, e.OrganizationId));
    }

    [Fact]
    public async Task Enabled_policy_really_blocks_creating_units_below()
    {
        // End to end with the access evaluator: the switch changes what the permission filter decides.
        var (tree, admin) = await RootAdminAsync("PO13");
        var schoolAdmin = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await EnableAsync(admin, tree.School1.Id);

        var evaluator = new Lms.Infrastructure.Configuration.PolicyEvaluator(
            factory.CreateDbContext(new TestCurrentContext { UserId = schoolAdmin.UserId, OrganizationId = tree.Org.Id, ScopePath = tree.School1.Path }),
            new TestCurrentContext { UserId = schoolAdmin.UserId, OrganizationId = tree.Org.Id, ScopePath = tree.School1.Path });

        Assert.Equal(tree.School1.Id, await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Create, tree.School1.Path));
    }
}
