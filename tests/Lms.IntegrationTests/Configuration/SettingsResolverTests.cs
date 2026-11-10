using System.Text.Json;
using Lms.Domain.Configuration;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Configuration;

[Collection(ApiCollection.Name)]
public sealed class SettingsResolverTests(LmsApiFactory factory)
{
    private static readonly SettingKey<string> Key = SettingKeys.UiDefaultCulture;

    private static (TestCurrentContext Ctx, SettingsResolver Resolver, Lms.Infrastructure.Persistence.LmsDbContext Db) Open(
        LmsApiFactory factory, ConfigurationTestTree tree)
    {
        var ctx = tree.AtRoot();
        var db = factory.CreateDbContext(ctx);
        return (ctx, new SettingsResolver(db, ctx), db);
    }

    [Fact]
    public async Task No_value_returns_default()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set1");
        var (_, resolver, db) = Open(factory, tree);
        await using var _db = db;

        var result = await resolver.ResolveAsync(Key, tree.Section.Path);

        Assert.Equal("ar", result.Value);
        Assert.Null(result.SourceOrgUnitId);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task Deepest_value_wins()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set2");
        var (_, resolver, db) = Open(factory, tree);
        await using var _db = db;

        await resolver.SetAsync(Key, tree.Root.Id, "en", isLocked: false);
        await resolver.SetAsync(Key, tree.School1.Id, "fr", isLocked: false);

        var atSection = await resolver.ResolveAsync(Key, tree.Section.Path);
        var atSchool2 = await resolver.ResolveAsync(Key, tree.School2.Path);

        Assert.Equal("fr", atSection.Value);
        Assert.Equal(tree.School1.Id, atSection.SourceOrgUnitId);
        Assert.Equal("en", atSchool2.Value);
        Assert.Equal(tree.Root.Id, atSchool2.SourceOrgUnitId);
    }

    [Fact]
    public async Task Locked_ancestor_wins_over_deeper_values()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set3");
        var (ctx, resolver, db) = Open(factory, tree);
        await using var _db = db;

        // A pre-existing lower value (written before the lock existed), then the root locks the key.
        db.Add(new OrgUnitSetting(tree.Org.Id, tree.School1.Id, tree.School1.Path, Key.Name, JsonSerializer.Serialize("fr"), false));
        await db.SaveChangesAsync();
        await resolver.SetAsync(Key, tree.Root.Id, "en", isLocked: true);

        var result = await resolver.ResolveAsync(Key, tree.Section.Path);

        Assert.Equal("en", result.Value);
        Assert.True(result.IsLocked);
        Assert.Equal(tree.Root.Id, result.SourceOrgUnitId);
        Assert.NotNull(ctx.UserId);
    }

    [Fact]
    public async Task Set_below_locked_ancestor_throws_SettingLockedException()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set4");
        var (_, resolver, db) = Open(factory, tree);
        await using var _db = db;
        await resolver.SetAsync(Key, tree.Root.Id, "en", isLocked: true);

        var ex = await Assert.ThrowsAsync<SettingLockedException>(
            () => resolver.SetAsync(Key, tree.School1.Id, "fr", isLocked: false));

        Assert.Equal(tree.Root.Id, ex.LockingOrgUnitId);
        Assert.Equal("en", (await resolver.ResolveAsync(Key, tree.School1.Path)).Value);
    }

    [Fact]
    public async Task Owner_of_a_lock_can_change_its_own_value()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set5");
        var (_, resolver, db) = Open(factory, tree);
        await using var _db = db;
        await resolver.SetAsync(Key, tree.Root.Id, "en", isLocked: true);

        await resolver.SetAsync(Key, tree.Root.Id, "fr", isLocked: true);

        Assert.Equal("fr", (await resolver.ResolveAsync(Key, tree.Section.Path)).Value);
        Assert.Equal(1, await db.Set<OrgUnitSetting>().CountAsync(x => x.OrgUnitId == tree.Root.Id));
    }

    [Fact]
    public async Task Soft_deleted_ancestor_setting_is_ignored()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set6");
        var (_, resolver, db) = Open(factory, tree);
        await using var _db = db;
        await resolver.SetAsync(Key, tree.Root.Id, "en", isLocked: true);
        var row = await db.Set<OrgUnitSetting>().SingleAsync(x => x.OrgUnitId == tree.Root.Id);
        db.Remove(row); // soft delete

        await db.SaveChangesAsync();

        var result = await resolver.ResolveAsync(Key, tree.Section.Path);
        Assert.Equal("ar", result.Value);
        Assert.Null(result.SourceOrgUnitId);
        // ... and the deleted lock no longer blocks lower writes.
        await resolver.SetAsync(Key, tree.School1.Id, "fr", isLocked: false);
    }

    [Fact]
    public async Task Other_client_setting_never_applies()
    {
        var a = await ConfigurationTestTree.SeedAsync(factory, "Set7A");
        var b = await ConfigurationTestTree.SeedAsync(factory, "Set7B");
        var (_, resolverB, dbB) = Open(factory, b);
        await using var _dbB = dbB;
        await resolverB.SetAsync(Key, b.Root.Id, "en", isLocked: true);

        var (_, resolverA, dbA) = Open(factory, a);
        await using var _dbA = dbA;

        // Client A asks about B's path: B's rows must not be visible, so A gets the default.
        Assert.Equal("ar", (await resolverA.ResolveAsync(Key, b.Section.Path)).Value);
    }

    [Fact]
    public async Task Setting_audited_and_persisted_with_arabic_value()
    {
        var tree = await ConfigurationTestTree.SeedAsync(factory, "Set8");
        var (_, resolver, db) = Open(factory, tree);
        await using var _db = db;

        await resolver.SetAsync(Key, tree.Root.Id, "مدرسة 🌟", isLocked: false);

        await using var fresh = factory.CreateDbContext(tree.AtRoot());
        var again = new SettingsResolver(fresh, tree.AtRoot());
        Assert.Equal("مدرسة 🌟", (await again.ResolveAsync(Key, tree.Root.Path)).Value);
        Assert.True(await fresh.AuditEntries.AnyAsync(x => x.EntityType == nameof(OrgUnitSetting)));
    }
}
