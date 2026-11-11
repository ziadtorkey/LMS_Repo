using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Access;

[Collection(ApiCollection.Name)]
public sealed class BuiltInRoleReconcilerTests(LmsApiFactory factory)
{
    /// <summary>Simulates a client provisioned under M0: defaults version 0 and only the M0 permission set.</summary>
    private async Task<AccessTree> SeedM0ClientAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var m0 = BuiltInRoleDefaults.M0Baseline;
        db.RolePermissions.RemoveRange(await db.RolePermissions
            .Where(p => p.OrganizationId == tree.Org.Id && !m0.Contains(p.Permission)).ToListAsync());
        foreach (var role in await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
        {
            db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 0;
        }

        await db.SaveChangesAsync();
        return tree;
    }

    private async Task ReconcileAsync()
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        await new BuiltInRoleReconciler(db).ReconcileAsync(CancellationToken.None);
    }

    private async Task<HashSet<string>> PermissionsOfAsync(AccessTree tree, string roleKey)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var roleId = tree.Roles[roleKey].Id;
        return (await db.RolePermissions.Where(p => p.RoleId == roleId).Select(p => p.Permission).ToListAsync()).ToHashSet();
    }

    [Fact]
    public async Task Reconcile_adds_the_M1_defaults_to_an_M0_provisioned_client()
    {
        var tree = await SeedM0ClientAsync("RC1");
        Assert.DoesNotContain(Permissions.LevelTypes.View, await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin));

        await ReconcileAsync();

        foreach (var key in tree.Roles.Keys)
        {
            Assert.Equal(
                BuiltInRoles.Get(key).DefaultPermissions.Order(),
                (await PermissionsOfAsync(tree, key)).Order());
        }
    }

    [Fact]
    public async Task Reconcile_never_re_adds_a_permission_an_admin_removed_and_never_removes_extras()
    {
        var tree = await SeedM0ClientAsync("RC2");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var teacher = tree.Roles[BuiltInRoles.Teacher].Id;
            // admin removed an M0 default, and granted a permission that is not a default of the role
            db.RolePermissions.RemoveRange(db.RolePermissions.Where(p => p.RoleId == teacher && p.Permission == Permissions.Files.Download));
            db.RolePermissions.Add(new RolePermission(tree.Org.Id, teacher, Permissions.Audit.View));
            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        var permissions = await PermissionsOfAsync(tree, BuiltInRoles.Teacher);
        Assert.DoesNotContain(Permissions.Files.Download, permissions);
        Assert.Contains(Permissions.Audit.View, permissions);
    }

    [Fact]
    public async Task Reconcile_runs_once_per_role_so_later_admin_edits_stick()
    {
        var tree = await SeedM0ClientAsync("RC3");
        await ReconcileAsync();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var admin = tree.Roles[BuiltInRoles.OrgAdmin].Id;
            db.RolePermissions.RemoveRange(db.RolePermissions.Where(p => p.RoleId == admin && p.Permission == Permissions.LevelTypes.View));
            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        Assert.DoesNotContain(Permissions.LevelTypes.View, await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin));
    }

    [Fact]
    public async Task Reconcile_adds_only_the_M2_defaults_to_a_version_1_client()
    {
        // A client provisioned under M1 (defaults version 1): no M2 permissions, and an admin removed an M1 default.
        var tree = await AccessTestData.SeedAsync(factory, "RC7");
        var introducedInM2 = BuiltInRoles.All.SelectMany(r => r.DefaultPermissions)
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) == 2).ToHashSet();
        Assert.NotEmpty(introducedInM2);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var admin = tree.Roles[BuiltInRoles.OrgAdmin].Id;
            db.RolePermissions.RemoveRange(await db.RolePermissions
                .Where(p => p.OrganizationId == tree.Org.Id
                    && (introducedInM2.Contains(p.Permission) || (p.RoleId == admin && p.Permission == Permissions.LevelTypes.View)))
                .ToListAsync());
            foreach (var role in await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
            {
                db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 1;
            }

            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        var orgAdmin = await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin);
        Assert.Contains(Permissions.Staff.Create, orgAdmin);
        Assert.Contains(Permissions.IdentityProviders.Manage, orgAdmin);
        Assert.DoesNotContain(Permissions.LevelTypes.View, orgAdmin);
        Assert.Contains(Permissions.Staff.View, await PermissionsOfAsync(tree, BuiltInRoles.Counselor));
        Assert.DoesNotContain(Permissions.Staff.View, await PermissionsOfAsync(tree, BuiltInRoles.Teacher));
    }

    [Fact]
    public async Task Newly_provisioned_roles_are_already_at_the_current_defaults_version()
    {
        var tree = await AccessTestData.SeedAsync(factory, "RC4");

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.All(await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync(),
            r => Assert.Equal(BuiltInRoleDefaults.CurrentVersion, r.DefaultsVersion));
    }

    [Fact]
    public async Task Custom_roles_are_not_touched()
    {
        var tree = await SeedM0ClientAsync("RC5");
        Guid customId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var custom = new Role(tree.Org.Id, null, "مخصص", "Custom", RoleBaseKind.Staff);
            db.Roles.Add(custom);
            await db.SaveChangesAsync();
            customId = custom.Id;
        }

        await ReconcileAsync();

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Empty(await read.RolePermissions.Where(p => p.RoleId == customId).ToListAsync());
    }

    [Fact]
    public async Task Startup_reconciler_updates_an_M0_client_in_the_database()
    {
        var tree = await SeedM0ClientAsync("RC6");
        var cs = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()).Database.GetConnectionString()!;

        await new BuiltInRoleStartupReconciler(cs, Microsoft.Extensions.Logging.Abstractions.NullLogger<BuiltInRoleStartupReconciler>.Instance)
            .RunAsync();

        Assert.Contains(Permissions.LevelTypes.View, await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin));
    }
}