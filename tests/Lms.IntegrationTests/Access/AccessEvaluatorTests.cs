using Lms.Domain.Access;
using Lms.Infrastructure.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.Extensions.Caching.Memory;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class AccessEvaluatorTests(LmsApiFactory factory)
{
    private async Task<AccessDecision> EvaluateAsync(
        TestCurrentContext context, string permission, string? resourcePath = null, TestPolicyEvaluator? policies = null)
    {
        await using var db = factory.CreateDbContext(context);
        var evaluator = new AccessEvaluator(db, context, policies ?? new TestPolicyEvaluator(), new MemoryCache(new MemoryCacheOptions()));
        return await evaluator.EvaluateAsync(permission, resourcePath, CancellationToken.None);
    }

    private static TestCurrentContext ActAs(AccessTree tree, RoleAssignment assignment, string scopePath) =>
        new()
        {
            UserId = assignment.UserId, OrganizationId = tree.Org.Id, AssignmentId = assignment.Id, ScopePath = scopePath,
        };

    [Fact]
    public async Task Granted_permission_within_scope_is_allowed()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE1");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        var decision = await EvaluateAsync(ActAs(tree, a, tree.Root.Path), Permissions.OrgUnits.View, tree.School1.Path);

        Assert.True(decision.Allowed);
        Assert.Equal(AccessDenialReason.None, decision.Reason);
    }

    [Fact]
    public async Task Resource_path_defaults_to_the_context_scope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE2");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, Guid.NewGuid());

        Assert.True((await EvaluateAsync(ActAs(tree, a, tree.School1.Path), Permissions.OrgUnits.View)).Allowed);
    }

    [Fact]
    public async Task Missing_permission_is_denied()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE3");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        var decision = await EvaluateAsync(ActAs(tree, a, tree.Root.Path), Permissions.OrgUnits.View);

        Assert.False(decision.Allowed);
        Assert.Equal(AccessDenialReason.MissingPermission, decision.Reason);
    }

    [Fact]
    public async Task Resource_outside_scope_is_denied_OutOfScope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE4");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Manager, Guid.NewGuid());

        var decision = await EvaluateAsync(ActAs(tree, a, tree.School1.Path), Permissions.OrgUnits.View, tree.Root.Path);

        Assert.Equal(AccessDenialReason.OutOfScope, decision.Reason);
    }

    [Fact]
    public async Task Unauthenticated_caller_is_denied_NotAuthenticated()
    {
        var decision = await EvaluateAsync(new TestCurrentContext(), Permissions.OrgUnits.View);

        Assert.Equal(AccessDenialReason.NotAuthenticated, decision.Reason);
    }

    [Fact]
    public async Task Authenticated_caller_without_active_context_is_denied()
    {
        var decision = await EvaluateAsync(new TestCurrentContext { UserId = Guid.NewGuid() }, Permissions.OrgUnits.View);

        Assert.Equal(AccessDenialReason.MissingPermission, decision.Reason);
    }

    [Fact]
    public async Task OrgAdmin_at_child_is_blocked_by_ancestor_policy()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE5");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var policies = new TestPolicyEvaluator();
        policies.Block(Permissions.OrgUnits.Create, tree.Root);

        var decision = await EvaluateAsync(ActAs(tree, a, tree.School1.Path), Permissions.OrgUnits.Create, policies: policies);

        Assert.Equal(AccessDenialReason.BlockedByPolicy, decision.Reason);
        Assert.Equal(tree.Root.Id, decision.BlockingOrgUnitId);
    }

    [Fact]
    public async Task Policy_on_a_sibling_branch_does_not_block()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE6");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var policies = new TestPolicyEvaluator();
        policies.Block(Permissions.OrgUnits.Create, tree.School1);

        Assert.True((await EvaluateAsync(ActAs(tree, a, tree.Root.Path), Permissions.OrgUnits.Create, tree.Root.Path, policies)).Allowed);
        Assert.False((await EvaluateAsync(ActAs(tree, a, tree.Root.Path), Permissions.OrgUnits.Create, tree.School1.Path, policies)).Allowed);
    }

    [Fact]
    public async Task Platform_admin_bypasses_role_and_scope_but_not_policies()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE7");
        var admin = TestCurrentContext.PlatformAdmin();
        var policies = new TestPolicyEvaluator();
        policies.Block(Permissions.OrgUnits.Create, tree.Root);

        Assert.True((await EvaluateAsync(admin, Permissions.Audit.Export, tree.School1.Path, policies)).Allowed);
        var blocked = await EvaluateAsync(admin, Permissions.OrgUnits.Create, tree.School1.Path, policies);
        Assert.Equal(AccessDenialReason.BlockedByPolicy, blocked.Reason);
    }

    [Fact]
    public async Task Role_permissions_are_cached_per_assignment()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AE8");
        var a = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var context = ActAs(tree, a, tree.Root.Path);
        var cache = new MemoryCache(new MemoryCacheOptions());

        await using (var db = factory.CreateDbContext(context))
        {
            Assert.True((await new AccessEvaluator(db, context, new TestPolicyEvaluator(), cache)
                .EvaluateAsync(Permissions.OrgUnits.View, null, CancellationToken.None)).Allowed);
        }

        // Remove the grant behind the evaluator's back: within the 60 s window the cached set still applies.
        await using (var admin = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            admin.RolePermissions.RemoveRange(admin.RolePermissions.Where(p =>
                p.RoleId == tree.Roles[BuiltInRoles.Manager].Id && p.Permission == Permissions.OrgUnits.View));
            await admin.SaveChangesAsync();
        }

        await using var db2 = factory.CreateDbContext(context);
        Assert.True((await new AccessEvaluator(db2, context, new TestPolicyEvaluator(), cache)
            .EvaluateAsync(Permissions.OrgUnits.View, null, CancellationToken.None)).Allowed);
    }
}