using System.Net;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;

namespace Lms.IntegrationTests.Assignments;

/// <summary>Role-blocking policies and person-kind rules on POST /api/assignments (M2 Task 8; Review Focus 5).</summary>
[Collection(ApiCollection.Name)]
public sealed class AssignmentPolicyTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private async Task<(AccessTree Tree, RoleAssignment Admin)> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        return (tree, admin);
    }

    private Task<HttpResponseMessage> AssignAsync(RoleAssignment caller, Role role, OrgUnit unit, string? language = null) =>
        _host.SendAsync(HttpMethod.Post, Routes.Assignments, caller,
            new CreateAssignmentRequest($"p{Guid.NewGuid():N}@example.com", "اسم", "Name", role.Id, unit.Id, null, null),
            language);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task CreateTeachers_at_school_blocks_assigning_Teacher_below_it_with_policy_message()
    {
        var (tree, admin) = await SetUpAsync("AP1");
        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateTeachers);

        var response = await AssignAsync(admin, tree.Roles[BuiltInRoles.Teacher], tree.School1, "en");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("BlockedByPolicy", body.GetProperty("reason").GetString());
        Assert.Equal(PolicyCatalog.BlockedMessageEn, body.GetProperty("detail").GetString());
        Assert.Equal(tree.Root.Id, body.GetProperty("blockingOrgUnitId").GetGuid());
    }

    [Fact]
    public async Task CreateTeachers_does_not_block_Counselor()
    {
        var (tree, admin) = await SetUpAsync("AP2");
        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateTeachers);

        var response = await AssignAsync(admin, tree.Roles[BuiltInRoles.Counselor], tree.School1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateAdmins_blocks_OrgAdmin_Manager_and_custom_role_with_Assignments_Create()
    {
        var (tree, admin) = await SetUpAsync("AP3");
        await _host.AddPolicyAsync(tree, tree.School1, PolicyCatalog.CreateAdmins);
        Role custom;
        Role harmless;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            custom = new Role(tree.Org.Id, null, "مخصص", "Custom admin", RoleBaseKind.Staff);
            harmless = new Role(tree.Org.Id, null, "عادي", "Custom plain", RoleBaseKind.Staff);
            db.AddRange(custom, harmless);
            db.Add(new RolePermission(tree.Org.Id, custom.Id, Permissions.Assignments.Create));
            db.Add(new RolePermission(tree.Org.Id, harmless.Id, Permissions.OrgUnits.View));
            await db.SaveChangesAsync();
        }

        foreach (var role in new[] { tree.Roles[BuiltInRoles.OrgAdmin], tree.Roles[BuiltInRoles.Manager], custom })
        {
            var response = await AssignAsync(admin, role, tree.School1);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("BlockedByPolicy", (await JsonAsync(response)).GetProperty("reason").GetString());
        }

        Assert.Equal(HttpStatusCode.Created, (await AssignAsync(admin, harmless, tree.School1)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await AssignAsync(admin, tree.Roles[BuiltInRoles.Teacher], tree.School1)).StatusCode);
    }

    [Theory]
    [InlineData(BuiltInRoles.Supervisor, "Assignments.Create")]
    [InlineData(BuiltInRoles.Counselor, "Roles.Update")]
    public async Task CreateAdmins_blocks_an_edited_built_in_role_holding_an_admin_marker(string key, string marker)
    {
        var (tree, admin) = await SetUpAsync("AP8" + key[..2]);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new RolePermission(tree.Org.Id, tree.Roles[key].Id, marker));
            await db.SaveChangesAsync();
        }

        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateAdmins);

        var response = await AssignAsync(admin, tree.Roles[key], tree.School1);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("BlockedByPolicy", (await JsonAsync(response)).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CreateAdmins_blocks_a_custom_role_holding_Roles_Update()
    {
        var (tree, admin) = await SetUpAsync("AP9");
        Role custom;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            custom = new Role(tree.Org.Id, null, "مخصص", "Role editor", RoleBaseKind.Staff);
            db.Add(custom);
            db.Add(new RolePermission(tree.Org.Id, custom.Id, Permissions.Roles.Update));
            await db.SaveChangesAsync();
        }

        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateAdmins);

        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(admin, custom, tree.School1)).StatusCode);
    }

    [Fact]
    public async Task Policy_at_sibling_does_not_block()
    {
        var (tree, admin) = await SetUpAsync("AP4");
        await _host.AddPolicyAsync(tree, tree.School2, PolicyCatalog.CreateTeachers);

        Assert.Equal(HttpStatusCode.Created,
            (await AssignAsync(admin, tree.Roles[BuiltInRoles.Teacher], tree.School1)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AssignAsync(admin, tree.Roles[BuiltInRoles.Teacher], tree.School2)).StatusCode);
    }

    [Fact]
    public async Task CreateStaff_blocks_Staff_Create_permission_via_existing_evaluator()
    {
        var (tree, _) = await SetUpAsync("AP5");
        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateStaff);
        await using var db = factory.CreateDbContext(TestCurrentContext.For(tree.Org.Id, tree.Root.Path));
        var evaluator = new PolicyEvaluator(db, TestCurrentContext.For(tree.Org.Id, tree.Root.Path));

        Assert.Equal(tree.Root.Id, await evaluator.FindBlockingUnitAsync(Permissions.Staff.Create, tree.School1.Path));
        Assert.Equal(tree.Root.Id, await evaluator.FindBlockingUnitAsync(Permissions.Staff.Import, tree.School1.Path));
        Assert.Null(await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Update, tree.School1.Path));
    }

    [Theory]
    [InlineData(BuiltInRoles.Student)]
    [InlineData(BuiltInRoles.Guardian)]
    public async Task Assigning_Student_or_Guardian_role_through_assignments_api_is_400_invalid_role_kind(string key)
    {
        var (tree, admin) = await SetUpAsync("AP6" + key[..1]);

        var response = await AssignAsync(admin, tree.Roles[key], tree.School1);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.invalid_role_kind, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Effective_policies_view_lists_role_only_policies()
    {
        var (tree, admin) = await SetUpAsync("AP7");
        await _host.AddPolicyAsync(tree, tree.Root, PolicyCatalog.CreateAdmins);

        var response = await _host.SendAsync(HttpMethod.Get, Routes.OrgUnitPolicies(tree.School1.Id), admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await JsonAsync(response)).EnumerateArray().ToList();
        var admins = list.Single(p => p.GetProperty("key").GetString() == PolicyCatalog.CreateAdmins);
        Assert.Equal(PolicyStates.Inherited, admins.GetProperty("state").GetString());
        Assert.Contains(list, p => p.GetProperty("key").GetString() == PolicyCatalog.CreateTeachers);
    }
}
