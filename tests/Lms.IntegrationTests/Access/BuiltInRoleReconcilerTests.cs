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
    /// <summary>
    /// The built-in role permission sets as the M0 provisioner created them (contracts #22, frozen here on purpose: a real
    /// M0 Manager had no Assignments.Create/Delete, which only D16 gave it in M1).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string[]> M0RoleLists = new Dictionary<string, string[]>
    {
        [BuiltInRoles.OrgAdmin] =
        [
            Permissions.OrgUnits.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.Update, Permissions.OrgUnits.Delete,
            Permissions.Roles.View, Permissions.Roles.Create, Permissions.Roles.Update, Permissions.Roles.Delete,
            Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Delete,
            Permissions.Settings.View, Permissions.Settings.Update, Permissions.Policies.View, Permissions.Policies.Update,
            Permissions.Audit.View, Permissions.Audit.Export, Permissions.Files.Upload, Permissions.Files.Download,
        ],
        [BuiltInRoles.Manager] =
        [
            Permissions.OrgUnits.View, Permissions.Roles.View, Permissions.Assignments.View, Permissions.Settings.View,
            Permissions.Policies.View, Permissions.Audit.View, Permissions.Files.Upload, Permissions.Files.Download,
        ],
        [BuiltInRoles.Supervisor] =
        [
            Permissions.OrgUnits.View, Permissions.Settings.View, Permissions.Policies.View, Permissions.Files.Upload,
            Permissions.Files.Download,
        ],
        [BuiltInRoles.Counselor] = [Permissions.Files.Upload, Permissions.Files.Download],
        [BuiltInRoles.Teacher] = [Permissions.Files.Upload, Permissions.Files.Download],
        [BuiltInRoles.Student] = [Permissions.Files.Upload, Permissions.Files.Download],
        [BuiltInRoles.Guardian] = [Permissions.Files.Download],
    };

    /// <summary>Simulates a client provisioned under M0: defaults version 0 and exactly each role's M0 permission list.</summary>
    private async Task<AccessTree> SeedM0ClientAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var roles = await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync();
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(p => p.OrganizationId == tree.Org.Id).ToListAsync());
        foreach (var role in roles)
        {
            db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 0;
            if (role.BuiltInKey is { } key && M0RoleLists.TryGetValue(key, out var m0))
            {
                db.RolePermissions.AddRange(m0.Select(p => new RolePermission(tree.Org.Id, role.Id, p)));
            }
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
    public async Task Reconcile_gives_an_M0_Manager_the_D16_assignment_permissions_it_never_had()
    {
        var tree = await SeedM0ClientAsync("RC8");
        var before = await PermissionsOfAsync(tree, BuiltInRoles.Manager);
        Assert.DoesNotContain(Permissions.Assignments.Create, before); // a real M0 Manager had neither
        Assert.DoesNotContain(Permissions.Assignments.Delete, before);

        await ReconcileAsync();

        var manager = await PermissionsOfAsync(tree, BuiltInRoles.Manager);
        Assert.Contains(Permissions.Assignments.Create, manager);
        Assert.Contains(Permissions.Assignments.Delete, manager);
        Assert.Equal(BuiltInRoles.Get(BuiltInRoles.Manager).DefaultPermissions.Order(), manager.Order());
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
    public async Task Reconcile_adds_only_the_M3_defaults_to_a_version_2_client()
    {
        // A client provisioned under M2 (defaults version 2): no M3 permissions, and an admin removed an M2 default.
        var tree = await AccessTestData.SeedAsync(factory, "RC8");
        var introducedInM3 = BuiltInRoles.All.SelectMany(r => r.DefaultPermissions)
            .Where(p => BuiltInRoleDefaults.IntroducedIn(p) == 3).ToHashSet();
        Assert.NotEmpty(introducedInM3);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var admin = tree.Roles[BuiltInRoles.OrgAdmin].Id;
            db.RolePermissions.RemoveRange(await db.RolePermissions
                .Where(p => p.OrganizationId == tree.Org.Id
                    && (introducedInM3.Contains(p.Permission) || (p.RoleId == admin && p.Permission == Permissions.Staff.Import)))
                .ToListAsync());
            foreach (var role in await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
            {
                db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 2;
            }

            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        var orgAdmin = await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin);
        Assert.Contains(Permissions.Courses.Create, orgAdmin);
        Assert.Contains(Permissions.Blueprints.Publish, orgAdmin);
        Assert.DoesNotContain(Permissions.Staff.Import, orgAdmin);
        Assert.Contains(Permissions.Blueprints.Edit, await PermissionsOfAsync(tree, BuiltInRoles.Manager));
        Assert.Contains(Permissions.Groups.Manage, await PermissionsOfAsync(tree, BuiltInRoles.Teacher));
        Assert.Contains(Permissions.Students.SelfEdit, await PermissionsOfAsync(tree, BuiltInRoles.Student));
        Assert.Contains(Permissions.Students.ViewChildren, await PermissionsOfAsync(tree, BuiltInRoles.Guardian));
        Assert.DoesNotContain(Permissions.Students.Import, await PermissionsOfAsync(tree, BuiltInRoles.Manager));
    }

    [Fact]
    public async Task Reconcile_adds_only_the_M4a_defaults_to_a_version_3_client()
    {
        // A client provisioned under M3 (defaults version 3): no Modules.* yet, and an admin removed an M3 default.
        var tree = await AccessTestData.SeedAsync(factory, "RC9");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var teacher = tree.Roles[BuiltInRoles.Teacher].Id;
            db.RolePermissions.RemoveRange(await db.RolePermissions
                .Where(p => p.OrganizationId == tree.Org.Id
                    && (p.Permission.StartsWith("Modules.") || (p.RoleId == teacher && p.Permission == Permissions.Groups.Manage)))
                .ToListAsync());
            foreach (var role in await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
            {
                db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 3;
            }

            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        var teacherPermissions = await PermissionsOfAsync(tree, BuiltInRoles.Teacher);
        Assert.Contains(Permissions.Modules.Manage, teacherPermissions);
        Assert.Contains(Permissions.Modules.View, teacherPermissions);
        Assert.DoesNotContain(Permissions.Groups.Manage, teacherPermissions);
        Assert.Contains(Permissions.Modules.Manage, await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin));
        Assert.Contains(Permissions.Modules.View, await PermissionsOfAsync(tree, BuiltInRoles.Student));
        Assert.DoesNotContain(Permissions.Modules.Manage, await PermissionsOfAsync(tree, BuiltInRoles.Supervisor));
        Assert.DoesNotContain(Permissions.Modules.View, await PermissionsOfAsync(tree, BuiltInRoles.Guardian));
    }

    [Fact]
    public async Task Reconcile_adds_only_the_M4a_wave_2_defaults_to_a_version_4_client()
    {
        // A client on M4a wave 1 (defaults version 4): no activity permissions yet, and an admin removed Modules.Manage
        // from the Teacher role (a version 4 default, which must stay removed).
        string[] wave2 = ["CourseAssignments.", "Quizzes.", "Library.", "Forums."];
        var tree = await AccessTestData.SeedAsync(factory, "RCA");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var teacher = tree.Roles[BuiltInRoles.Teacher].Id;
            var rows = await db.RolePermissions.Where(p => p.OrganizationId == tree.Org.Id).ToListAsync();
            db.RolePermissions.RemoveRange(rows.Where(p => wave2.Any(w => p.Permission.StartsWith(w, StringComparison.Ordinal))
                || (p.RoleId == teacher && p.Permission == Permissions.Modules.Manage)));
            foreach (var role in await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
            {
                db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 4;
            }

            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        var teacherPermissions = await PermissionsOfAsync(tree, BuiltInRoles.Teacher);
        Assert.Contains(Permissions.CourseAssignments.Grade, teacherPermissions);
        Assert.Contains(Permissions.Quizzes.Manage, teacherPermissions);
        Assert.Contains(Permissions.Forums.Moderate, teacherPermissions);
        Assert.Contains(Permissions.Library.Manage, teacherPermissions);
        Assert.DoesNotContain(Permissions.Modules.Manage, teacherPermissions);
        Assert.DoesNotContain(Permissions.CourseAssignments.Submit, teacherPermissions);

        var studentPermissions = await PermissionsOfAsync(tree, BuiltInRoles.Student);
        Assert.Contains(Permissions.CourseAssignments.Submit, studentPermissions);
        Assert.Contains(Permissions.Quizzes.Attempt, studentPermissions);
        Assert.Contains(Permissions.Forums.Post, studentPermissions);
        Assert.DoesNotContain(Permissions.Forums.Moderate, studentPermissions);

        Assert.Contains(Permissions.CourseAssignments.Grade, await PermissionsOfAsync(tree, BuiltInRoles.Manager));
        Assert.Contains(Permissions.Forums.Moderate, await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin));
        Assert.Contains(Permissions.Library.Manage, await PermissionsOfAsync(tree, BuiltInRoles.Supervisor));
        Assert.DoesNotContain(Permissions.Quizzes.Manage, await PermissionsOfAsync(tree, BuiltInRoles.Supervisor));
        Assert.Contains(Permissions.Library.View, await PermissionsOfAsync(tree, BuiltInRoles.Counselor));
        Assert.DoesNotContain(await PermissionsOfAsync(tree, BuiltInRoles.Guardian),
            p => wave2.Any(w => p.StartsWith(w, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Reconcile_adds_only_the_M4b_wave_3a_defaults_to_a_version_5_client()
    {
        // A client on M4a wave 2 (defaults version 5): no live-session permissions yet, and an admin removed Forums.Post
        // from the Student role (a version 5 default, which must stay removed).
        var tree = await AccessTestData.SeedAsync(factory, "RCB");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var student = tree.Roles[BuiltInRoles.Student].Id;
            var rows = await db.RolePermissions.Where(p => p.OrganizationId == tree.Org.Id).ToListAsync();
            db.RolePermissions.RemoveRange(rows.Where(p => p.Permission.StartsWith("LiveSessions.", StringComparison.Ordinal)
                || (p.RoleId == student && p.Permission == Permissions.Forums.Post)));
            foreach (var role in await db.Roles.Where(r => r.OrganizationId == tree.Org.Id).ToListAsync())
            {
                db.Entry(role).Property(nameof(Role.DefaultsVersion)).CurrentValue = 5;
            }

            await db.SaveChangesAsync();
        }

        await ReconcileAsync();

        var teacherPermissions = await PermissionsOfAsync(tree, BuiltInRoles.Teacher);
        Assert.Contains(Permissions.LiveSessions.View, teacherPermissions);
        Assert.Contains(Permissions.LiveSessions.Manage, teacherPermissions);

        var studentPermissions = await PermissionsOfAsync(tree, BuiltInRoles.Student);
        Assert.Contains(Permissions.LiveSessions.View, studentPermissions);
        Assert.DoesNotContain(Permissions.LiveSessions.Manage, studentPermissions);
        Assert.DoesNotContain(Permissions.Forums.Post, studentPermissions);

        Assert.Contains(Permissions.LiveSessions.Manage, await PermissionsOfAsync(tree, BuiltInRoles.OrgAdmin));
        Assert.Contains(Permissions.LiveSessions.Manage, await PermissionsOfAsync(tree, BuiltInRoles.Manager));
        var supervisorPermissions = await PermissionsOfAsync(tree, BuiltInRoles.Supervisor);
        Assert.Contains(Permissions.LiveSessions.View, supervisorPermissions);
        Assert.DoesNotContain(Permissions.LiveSessions.Manage, supervisorPermissions);
        Assert.DoesNotContain(await PermissionsOfAsync(tree, BuiltInRoles.Counselor),
            p => p.StartsWith("LiveSessions.", StringComparison.Ordinal));
        Assert.DoesNotContain(await PermissionsOfAsync(tree, BuiltInRoles.Guardian),
            p => p.StartsWith("LiveSessions.", StringComparison.Ordinal));
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