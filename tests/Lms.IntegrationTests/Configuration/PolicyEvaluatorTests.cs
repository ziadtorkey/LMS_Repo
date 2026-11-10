using Lms.Domain.Configuration;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;

namespace Lms.IntegrationTests.Configuration;

[Collection(ApiCollection.Name)]
public sealed class PolicyEvaluatorTests(LmsApiFactory factory)
{
    private async Task<(PolicyEvaluator Evaluator, Lms.Infrastructure.Persistence.LmsDbContext Db)> OpenAsync(
        ConfigurationTestTree tree, Lms.Domain.Organizations.OrgUnit? policyUnit, string policyKey = PolicyCatalog.CreateOrgUnits)
    {
        var ctx = tree.AtRoot();
        var db = factory.CreateDbContext(ctx);
        if (policyUnit is not null)
        {
            db.Add(new OrgUnitPolicy(tree.Org.Id, policyUnit.Id, policyUnit.Path, policyKey));
            await db.SaveChangesAsync();
        }

        return (new PolicyEvaluator(db, ctx), db);
    }

    [Fact]
    public async Task Policy_on_ancestor_blocks_permission_in_descendant()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Pol1");
        var (evaluator, db) = await OpenAsync(tree, tree.Root);
        await using var _ = db;

        var blocking = await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Create, tree.Section.Path);

        Assert.Equal(tree.Root.Id, blocking);
    }

    [Fact]
    public async Task Highest_blocking_unit_is_returned()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Pol2");
        var (evaluator, db) = await OpenAsync(tree, tree.School1);
        await using var _ = db;
        db.Add(new OrgUnitPolicy(tree.Org.Id, tree.Root.Id, tree.Root.Path, PolicyCatalog.CreateOrgUnits));
        await db.SaveChangesAsync();

        Assert.Equal(tree.Root.Id, await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Create, tree.Section.Path));
    }

    [Fact]
    public async Task Policy_on_the_unit_itself_blocks_it()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Pol3");
        var (evaluator, db) = await OpenAsync(tree, tree.School1);
        await using var _ = db;

        Assert.Equal(tree.School1.Id, await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Create, tree.School1.Path));
    }

    [Fact]
    public async Task Policy_on_sibling_does_not_block()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Pol4");
        var (evaluator, db) = await OpenAsync(tree, tree.School2);
        await using var _ = db;

        Assert.Null(await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Create, tree.Section.Path));
    }

    [Fact]
    public async Task Policy_without_catalog_mapping_does_not_block_unrelated_permission()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Pol5");
        var (evaluator, db) = await OpenAsync(tree, tree.Root);
        await using var _ = db;
        db.Add(new OrgUnitPolicy(tree.Org.Id, tree.Root.Id, tree.Root.Path, "NoSuchPolicy"));
        await db.SaveChangesAsync();

        Assert.Null(await evaluator.FindBlockingUnitAsync(Permissions.Roles.View, tree.Section.Path));
    }

    [Fact]
    public async Task Soft_deleted_policy_does_not_block()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Pol6");
        var (evaluator, db) = await OpenAsync(tree, tree.Root);
        await using var _ = db;
        var row = db.Set<OrgUnitPolicy>().Single(x => x.OrgUnitId == tree.Root.Id);
        db.Remove(row);
        await db.SaveChangesAsync();

        Assert.Null(await evaluator.FindBlockingUnitAsync(Permissions.OrgUnits.Create, tree.Section.Path));
    }

    [Fact]
    public async Task Other_client_policy_never_blocks()
    {
        var a = await ConfigurationTestTree.SeedAsync(factory, "Pol7A");
        var b = await ConfigurationTestTree.SeedAsync(factory, "Pol7B");
        var (_, dbB) = await OpenAsync(b, b.Root);
        await using var _b = dbB;
        var (evaluatorA, dbA) = await OpenAsync(a, null);
        await using var _a = dbA;

        Assert.Null(await evaluatorA.FindBlockingUnitAsync(Permissions.OrgUnits.Create, b.Section.Path));
    }
}
