using System.Net;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Staff;

/// <summary>Role-blocking policies on staff create (M2 Task 10; Review Focus 5).</summary>
[Collection(ApiCollection.Name)]
public sealed class StaffPolicyTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        return (tree, await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid()));
    }

    private Task<HttpResponseMessage> CreateAsync(RoleAssignment admin, AccessTree tree, string roleKey, Guid unitId) =>
        _host.SendAsync(HttpMethod.Post, Routes.Staff, admin, new CreateStaffRequest(
            "موظف", "Staff", $"p{Guid.NewGuid():N}@lms.test", null, null, null, null, null, unitId, null,
            [new StaffRoleInput(tree.Roles[roleKey].Id, unitId, null, null)]));

    private async Task AssertBlockedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CreateTeachers_policy_blocks_teacher_role_on_create_and_leaves_no_profile()
    {
        var (tree, admin) = await SetUpAsync("SP1");
        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateTeachers);

        await AssertBlockedAsync(await CreateAsync(admin, tree, BuiltInRoles.Teacher, tree.School1.Id));
        // Other staff roles are not blocked by it.
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(admin, tree, BuiltInRoles.Counselor, tree.School1.Id)).StatusCode);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<Lms.Domain.People.StaffProfile>().CountAsync(p => p.OrganizationId == tree.Org.Id));
    }

    [Fact]
    public async Task CreateAdmins_policy_blocks_admin_role_on_create()
    {
        var (tree, admin) = await SetUpAsync("SP2");
        await _host.AddPolicyAsync(tree, tree.School1, PolicyCatalog.CreateAdmins);

        await AssertBlockedAsync(await CreateAsync(admin, tree, BuiltInRoles.Manager, tree.School1.Id));
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(admin, tree, BuiltInRoles.Teacher, tree.School1.Id)).StatusCode);
    }
}
