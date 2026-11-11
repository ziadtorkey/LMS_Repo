using Lms.Domain.Access;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Access;

/// <summary>The shared grant rule used by every path that creates an assignment (M2 Task 8).</summary>
[Collection(ApiCollection.Name)]
public sealed class RoleGrantGuardTests(LmsApiFactory factory)
{
    private static async Task AddPolicyAsync(LmsApiFactory f, AccessTree tree, Lms.Domain.Organizations.OrgUnit unit, string key)
    {
        await using var db = f.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new Lms.Domain.Configuration.OrgUnitPolicy(tree.Org.Id, unit.Id, unit.Path, key));
        await db.SaveChangesAsync();
    }

    private sealed class LmsDbContextHolder(RoleGrantGuard guard) : IAsyncDisposable
    {
        public RoleGrantGuard Guard { get; } = guard;
        public required Lms.Infrastructure.Persistence.LmsDbContext Db { get; init; }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private LmsDbContextHolder Holder(AccessTree tree)
    {
        var context = TestCurrentContext.For(tree.Org.Id, tree.Root.Path);
        var db = factory.CreateDbContext(context);
        return new LmsDbContextHolder(new RoleGrantGuard(new PolicyEvaluator(db, context))) { Db = db };
    }

    [Fact]
    public async Task Blocked_role_is_denied_with_the_blocking_unit_and_others_are_allowed()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RG1");
        await AddPolicyAsync(factory, tree, tree.Root, PolicyCatalog.CreateTeachers);
        await using var holder = Holder(tree);

        var blocked = await holder.Guard.CheckAsync(tree.Roles[BuiltInRoles.Teacher], tree.School1, default);
        var allowed = await holder.Guard.CheckAsync(tree.Roles[BuiltInRoles.Supervisor], tree.School1, default);

        Assert.False(blocked.Allowed);
        Assert.Equal(AccessDenialReason.BlockedByPolicy, blocked.Reason);
        Assert.Equal(tree.Root.Id, blocked.BlockingOrgUnitId);
        Assert.True(allowed.Allowed);
    }

    [Fact]
    public async Task Role_of_another_client_is_out_of_scope()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RG2");
        var other = await AccessTestData.SeedAsync(factory, "RG3");
        await using var holder = Holder(tree);

        var decision = await holder.Guard.CheckAsync(other.Roles[BuiltInRoles.Teacher], tree.School1, default);

        Assert.False(decision.Allowed);
        Assert.Equal(AccessDenialReason.OutOfScope, decision.Reason);
    }
}
