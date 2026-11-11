using Lms.Domain.Access;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Microsoft.AspNetCore.Identity;
using Lms.Infrastructure.Persistence;
using Lms.Infrastructure.Seeding;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Lms.IntegrationTests.Seeding;

[Collection(ApiCollection.Name)]
public sealed class DevSeederTests(LmsApiFactory factory)
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Lms.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static LmsDbContext Open(string connectionString) =>
        LmsDbContextFactory.Create(connectionString, TestCurrentContext.PlatformAdmin());

    private const string Password = "DevPass123";
    private const string AdminEmail = "admin@lms.test";

    private static DevSeeder SeederFor(string connectionString, string environment = "Development", bool withUsers = true) =>
        new(connectionString, new FakeEnvironment(environment),
            withUsers ? new SeedOptions(AdminEmail, Password) : new SeedOptions(null, null));

    [Fact]
    public async Task Seeding_creates_the_school_group_and_university_trees()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();

        Assert.True(await SeederFor(cs).SeedAsync());

        await using var db = Open(cs);
        var orgs = await db.Organizations.OrderBy(o => o.NameEn).ToListAsync();
        Assert.Equal(["Altagamo3 Schools", "MNF University"], orgs.Select(o => o.NameEn));
        Assert.Equal(OrganizationType.SchoolGroup, orgs[0].Type);
        Assert.Equal(OrganizationType.University, orgs[1].Type);
        Assert.Equal("مجموعة مدارس التجمع", orgs[0].NameAr);
        Assert.Equal("جامعة MNF", orgs[1].NameAr);

        var school = await UnitsOf(db, orgs[0].Id);
        Assert.Equal(
            ["KG", "KG-S1", "KG-S2", "PRI", "PRI-S1", "PRI-S2", "PRI-S3", "S"], school.Keys.Order());
        Assert.Equal(0, school["S"].Depth);
        Assert.Equal(school["S"].Id, school["KG"].ParentId);
        Assert.Equal(2, school["KG-S1"].Depth);
        Assert.True(school["KG"].IsLevelTransfer);
        Assert.True(school["PRI"].IsLevelTransfer);
        Assert.False(school["KG-S1"].IsLevelTransfer);

        var univ = await UnitsOf(db, orgs[1].Id);
        Assert.Equal(
            ["FCI", "FCI-1", "FCI-1-BIO", "FCI-1-GEN", "FCI-1-SOFT", "FCI-2", "FCI-2-BIO", "FCI-2-GEN", "FCI-2-SOFT",
             "FE", "FE-1", "FE-1-GEN", "FE-2", "FE-2-ELEC", "MNF"],
            univ.Keys.Order());
        Assert.Equal(3, univ["FCI-1-SOFT"].Depth);
        Assert.True(univ["FCI-1"].IsLevelTransfer);
        Assert.True(univ["FCI-2"].IsLevelTransfer);
        Assert.False(univ["FCI"].IsLevelTransfer);

        var levels = await db.LevelTypes.Where(l => l.OrganizationId == orgs[1].Id).OrderBy(l => l.Order).Select(l => l.NameEn).ToListAsync();
        Assert.Equal(["University", "Faculty", "Year", "Specialization"], levels);
    }

    [Fact]
    public async Task Both_clients_get_the_seven_built_in_roles_with_type_specific_labels()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();

        await using var db = Open(cs);
        var managers = await db.Roles.Where(r => r.BuiltInKey == "Manager").Select(r => r.NameEn).ToListAsync();

        Assert.Equal(14, await db.Roles.CountAsync());
        Assert.Equal(["Dean", "School Manager"], managers.Order());
    }

    [Fact]
    public async Task Seeding_twice_is_idempotent()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        var seeder = SeederFor(cs);

        await seeder.SeedAsync();
        await using (var first = Open(cs))
        {
            var before = (await first.Organizations.CountAsync(), await first.LevelTypes.CountAsync(),
                await first.OrgUnits.CountAsync(), await first.Roles.CountAsync(), await first.RolePermissions.CountAsync());
            await seeder.SeedAsync();
            await SeederFor(cs).SeedAsync();

            await using var second = Open(cs);
            var after = (await second.Organizations.CountAsync(), await second.LevelTypes.CountAsync(),
                await second.OrgUnits.CountAsync(), await second.Roles.CountAsync(), await second.RolePermissions.CountAsync());
            Assert.Equal(before, after);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public async Task Seeder_does_nothing_outside_Development(string environment)
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();

        Assert.False(await SeederFor(cs, environment).SeedAsync());

        await using var db = Open(cs);
        Assert.Empty(await db.Organizations.ToListAsync());
    }

    private static async Task<List<(string Email, string Role, string Unit)>> AssignmentsAsync(LmsDbContext db) =>
        (await (
            from a in db.RoleAssignments
            join u in db.Users on a.UserId equals u.Id
            join r in db.Roles on a.RoleId equals r.Id
            join unit in db.OrgUnits on a.OrgUnitId equals unit.Id
            select new { u.Email, r.BuiltInKey, unit.Code }).ToListAsync())
        .Select(x => (x.Email!, x.BuiltInKey!, x.Code)).ToList();

    [Fact]
    public async Task Seeding_creates_a_platform_admin_and_one_user_per_built_in_role()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();

        await using var db = Open(cs);
        var admin = await db.Users.SingleAsync(x => x.Email == AdminEmail);
        Assert.True(admin.IsPlatformAdmin);
        Assert.True(admin.EmailConfirmed);
        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(admin, admin.PasswordHash!, Password));

        var assignments = await AssignmentsAsync(db);
        foreach (var (prefix, role) in new[]
                 {
                     ("orgadmin", "OrgAdmin"), ("manager", "Manager"), ("supervisor", "Supervisor"),
                     ("counselor", "Counselor"), ("teacher", "Teacher"), ("student", "Student"), ("guardian", "Guardian"),
                 })
        {
            var mine = assignments.Where(a => a.Email == $"{prefix}@altagamo3.test").ToList();
            Assert.Single(mine);
            Assert.Equal(role, mine[0].Role);
        }

        Assert.All(await db.Users.Where(x => x.Email!.EndsWith("@altagamo3.test")).ToListAsync(), x =>
        {
            Assert.False(x.IsPlatformAdmin);
            Assert.True(x.LockoutEnabled);
            Assert.NotEmpty(x.DisplayNameAr);
            Assert.NotEmpty(x.DisplayNameEn);
        });
    }

    [Fact]
    public async Task Multi_context_user_is_teacher_in_KG_and_supervisor_in_Primary()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();

        await using var db = Open(cs);
        var mine = (await AssignmentsAsync(db)).Where(a => a.Email == "multi@altagamo3.test").OrderBy(a => a.Role).ToList();

        Assert.Equal([("multi@altagamo3.test", "Supervisor", "PRI"), ("multi@altagamo3.test", "Teacher", "KG")], mine);
    }

    [Fact]
    public async Task Lockout_user_is_a_teacher_in_KG_used_only_by_the_lockout_e2e()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();

        await using var db = Open(cs);
        var mine = (await AssignmentsAsync(db)).Where(a => a.Email == "lockout-e2e@altagamo3.test").ToList();

        Assert.Equal([("lockout-e2e@altagamo3.test", "Teacher", "KG")], mine);
    }

    [Fact]
    public async Task Notification_user_is_a_teacher_in_KG_used_only_by_the_notification_e2e()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();

        await using var db = Open(cs);
        var mine = (await AssignmentsAsync(db)).Where(a => a.Email == "notify-e2e@altagamo3.test").ToList();

        Assert.Equal([("notify-e2e@altagamo3.test", "Teacher", "KG")], mine);
    }

    [Theory]
    [InlineData("deactivated-e2e@altagamo3.test", UserStatus.Deactivated)]
    [InlineData("expired-e2e@altagamo3.test", UserStatus.Expired)]
    [InlineData("withdrawn-e2e@altagamo3.test", UserStatus.Withdrawn)]
    public async Task Blocked_status_users_exist_only_for_the_blocked_login_e2e(string email, UserStatus status)
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();

        await using var db = Open(cs);
        var user = await db.Users.SingleAsync(x => x.Email == email);
        Assert.Equal(status, user.Status);
        Assert.Equal([(email, "Teacher", "KG")], (await AssignmentsAsync(db)).Where(a => a.Email == email).ToList());
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_users_or_assignments_and_keeps_passwords_working()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();
        await SeederFor(cs).SeedAsync();
        await using var first = Open(cs);
        var users = await first.Users.CountAsync();
        var assignments = await first.RoleAssignments.CountAsync();

        await SeederFor(cs).SeedAsync();

        await using var second = Open(cs);
        Assert.Equal(users, await second.Users.CountAsync());
        Assert.Equal(assignments, await second.RoleAssignments.CountAsync());
        Assert.Equal(14, users); // admin + 7 role users + multi + lockout-e2e + notify-e2e + 3 blocked-status e2e users
    }

    [Fact]
    public async Task Without_a_configured_password_no_users_are_created()
    {
        var cs = await factory.CreateIsolatedDatabaseAsync();

        await SeederFor(cs, withUsers: false).SeedAsync();

        await using var db = Open(cs);
        Assert.Empty(await db.Users.ToListAsync());
        Assert.NotEmpty(await db.Organizations.ToListAsync());
    }
    private static async Task<Dictionary<string, OrgUnit>> UnitsOf(LmsDbContext db, Guid organizationId) =>
        (await db.OrgUnits.Where(u => u.OrganizationId == organizationId).ToListAsync()).ToDictionary(u => u.Code);
}