using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Persistence;

[Collection(ApiCollection.Name)]
public sealed class IsolationTests(LmsApiFactory factory)
{
    private sealed record Tree(
        Organization Org, LevelType Level, OrgUnit Root, OrgUnit School1, OrgUnit School2);

    /// <summary>Seeds one client with Root → School1, School2 using a platform-admin context.</summary>
    private async Task<Tree> SeedClientAsync(string name, Guid? school1Id = null, Guid? school2Id = null)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var org = new Organization(Guid.NewGuid(), "مجموعة " + name, name, OrganizationType.SchoolGroup);
        var level = new LevelType(org.Id, "مستوى", "Level", 1, true);
        var root = OrgUnit.CreateRoot(org.Id, level.Id, "جذر", "Root " + name, "R-" + name);
        var s1 = OrgUnit.CreateChild(root, level.Id, "مدرسة 1", "School1 " + name, "S1");
        var s2 = OrgUnit.CreateChild(root, level.Id, "مدرسة 2", "School2 " + name, "S2");
        db.AddRange(org, level, root, s1, s2);
        await db.SaveChangesAsync();
        return new Tree(org, level, root, s1, s2);
    }

    private LmsDbContext As(Tree tree, OrgUnit? scope) =>
        factory.CreateDbContext(TestCurrentContext.For(tree.Org.Id, scope?.Path));

    [Fact]
    public async Task Client_A_context_never_sees_client_B_units()
    {
        var a = await SeedClientAsync("A1");
        var b = await SeedClientAsync("B1");

        await using var db = As(a, a.Root);
        var units = await db.OrgUnits.ToListAsync();

        Assert.NotEmpty(units);
        Assert.All(units, x => Assert.Equal(a.Org.Id, x.OrganizationId));
        Assert.DoesNotContain(units, x => x.Id == b.Root.Id);
    }

    [Fact]
    public async Task Client_A_context_never_sees_client_B_level_types_or_organization()
    {
        var a = await SeedClientAsync("A2");
        var b = await SeedClientAsync("B2");

        await using var db = As(a, a.Root);

        Assert.Equal([a.Level.Id], await db.LevelTypes.Select(x => x.Id).ToListAsync());
        Assert.Equal([a.Org.Id], await db.Organizations.Select(x => x.Id).ToListAsync());
        Assert.DoesNotContain(await db.LevelTypes.ToListAsync(), x => x.Id == b.Level.Id);
    }

    [Fact]
    public async Task School1_scope_sees_own_subtree_but_not_School2()
    {
        var a = await SeedClientAsync("A3");

        await using var db = As(a, a.School1);
        var ids = await db.OrgUnits.Select(x => x.Id).ToListAsync();

        Assert.Equal([a.School1.Id], ids);
    }

    [Fact]
    public async Task Sibling_whose_id_shares_a_hex_prefix_is_not_in_scope()
    {
        // ids differ only in the last hex digit: a non-segment-safe prefix match would leak.
        var a = await SeedClientAsync(
            "A4", Guid.Parse("11111111-1111-7111-8111-111111111111"), Guid.Parse("11111111-1111-7111-8111-111111111112"));

        await using var db = As(a, a.School1);

        Assert.Equal([a.School1.Id], await db.OrgUnits.Select(x => x.Id).ToListAsync());
    }

    [Fact]
    public async Task Root_scope_sees_all_units_of_its_client()
    {
        var a = await SeedClientAsync("A5");

        await using var db = As(a, a.Root);

        Assert.Equal(3, await db.OrgUnits.CountAsync());
    }

    [Fact]
    public async Task Null_scope_returns_no_scoped_rows()
    {
        var a = await SeedClientAsync("A6");

        await using var db = As(a, null);

        Assert.Empty(await db.OrgUnits.ToListAsync());
    }

    [Fact]
    public async Task No_organization_returns_no_tenant_rows()
    {
        await SeedClientAsync("A7");

        await using var db = factory.CreateDbContext(new TestCurrentContext { UserId = Guid.NewGuid() });

        Assert.Empty(await db.LevelTypes.ToListAsync());
        Assert.Empty(await db.OrgUnits.ToListAsync());
        Assert.Empty(await db.Organizations.ToListAsync());
    }

    [Fact]
    public async Task Soft_deleted_unit_is_hidden_and_sibling_visible_and_restore_brings_it_back()
    {
        var a = await SeedClientAsync("A8");

        await using (var db = As(a, a.Root))
        {
            var school1 = await db.OrgUnits.SingleAsync(x => x.Id == a.School1.Id);
            db.OrgUnits.Remove(school1);
            await db.SaveChangesAsync();
        }

        await using (var db = As(a, a.Root))
        {
            var ids = await db.OrgUnits.Select(x => x.Id).ToListAsync();
            Assert.DoesNotContain(a.School1.Id, ids);
            Assert.Contains(a.School2.Id, ids);

            var row = await db.OrgUnits.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == a.School1.Id && x.OrganizationId == a.Org.Id); // test: inspect soft-deleted row
            Assert.True(row.IsDeleted);
            Assert.NotNull(row.DeletedAt);
            row.Restore();
            await db.SaveChangesAsync();
        }

        await using (var db = As(a, a.Root))
        {
            Assert.Contains(a.School1.Id, await db.OrgUnits.Select(x => x.Id).ToListAsync());
        }
    }

    [Fact]
    public async Task Insert_stamps_OrganizationId_from_context()
    {
        var a = await SeedClientAsync("A9");

        await using (var db = As(a, a.Root))
        {
            db.LevelTypes.Add(new LevelType(Guid.Empty, "ك", "Stamped", 2, false));
            await db.SaveChangesAsync();
        }

        await using var read = As(a, a.Root);
        var stamped = await read.LevelTypes.SingleAsync(x => x.NameEn == "Stamped");
        Assert.Equal(a.Org.Id, stamped.OrganizationId);
        Assert.NotEqual(default, stamped.CreatedAt);
        Assert.NotNull(stamped.CreatedBy);
    }

    [Fact]
    public async Task Insert_with_other_client_OrganizationId_is_rejected()
    {
        var a = await SeedClientAsync("A10");
        var b = await SeedClientAsync("B10");

        await using var db = As(a, a.Root);
        db.LevelTypes.Add(new LevelType(b.Org.Id, "ك", "Forged", 2, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Arabic_and_emoji_names_round_trip()
    {
        var a = await SeedClientAsync("A11");
        const string name = "مدرسة النور 🌟";

        await using (var db = As(a, a.Root))
        {
            db.LevelTypes.Add(new LevelType(Guid.Empty, name, name, 3, false));
            await db.SaveChangesAsync();
        }

        await using var read = As(a, a.Root);
        var row = await read.LevelTypes.SingleAsync(x => x.Order == 3);
        Assert.Equal(name, row.NameAr);
        Assert.Equal(name, row.NameEn);
    }

    [Fact]
    public async Task OrgUnit_path_column_is_ascii()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var charset = await db.Database.SqlQueryRaw<string>(
            "SELECT CHARACTER_SET_NAME AS Value FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OrgUnits' AND COLUMN_NAME = 'Path'").SingleAsync();

        Assert.Equal("ascii", charset);
    }
}