using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.OrgUnits;

[Collection(ApiCollection.Name)]
public sealed class OrgUnitDeleteRulesTests(LmsApiFactory factory)
{
    private sealed class BlockingUsageCheck : IOrgUnitUsageCheck
    {
        public Task<string?> FindUsageAsync(OrgUnit unit, CancellationToken ct) => Task.FromResult<string?>("unit_has_students");
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Delete_with_children_409()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DR1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        using var host = new OrgUnitTestHost(factory);

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.Root.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        // the root is refused first; use a unit that has a child
        var deeper = await host.AddDeeperLevelAsync(tree);
        await host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            new CreateOrgUnitRequest(tree.School1.Id, deeper.Id, "ب", "Kid", "KID"));

        var withChild = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School1.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, withChild.StatusCode);
        Assert.Equal(ProblemCodes.unit_has_children, (await JsonAsync(withChild)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Delete_with_assignments_409()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DR2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        using var host = new OrgUnitTestHost(factory);

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School1.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.unit_has_assignments, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Registered_usage_check_blocks_delete_unit_in_use()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DR3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        using var host = new OrgUnitTestHost(factory, s => s.AddScoped<IOrgUnitUsageCheck, BlockingUsageCheck>());

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(ProblemCodes.unit_in_use, body.GetProperty("code").GetString());
        Assert.Equal("unit_has_students", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Delete_leaf_soft_deletes_and_writes_audit()
    {
        var tree = await AccessTestData.SeedAsync(factory, "DR4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        using var host = new OrgUnitTestHost(factory);

        var response = await host.SendAsync(HttpMethod.Delete, Routes.OrgUnit(tree.School2.Id), admin);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.False(await db.OrgUnits.AnyAsync(u => u.Id == tree.School2.Id));
        var row = await db.OrgUnits.IgnoreQueryFilters().SingleAsync(u => u.Id == tree.School2.Id); // test: inspect the soft-deleted row
        Assert.True(row.IsDeleted);
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityId == tree.School2.Id && e.Action == AuditAction.Deleted));
        Assert.Equal(HttpStatusCode.NotFound, (await host.SendAsync(HttpMethod.Get, Routes.OrgUnit(tree.School2.Id), admin)).StatusCode);

        // its code is free again
        var deeper = await host.AddDeeperLevelAsync(tree);
        var reuse = await host.SendAsync(HttpMethod.Post, Routes.OrgUnits, admin,
            new CreateOrgUnitRequest(tree.Root.Id, deeper.Id, "ب", "Again", tree.School2.Code));
        Assert.Equal(HttpStatusCode.Created, reuse.StatusCode);
    }
}