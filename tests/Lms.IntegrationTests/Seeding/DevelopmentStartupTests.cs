using System.Net;
using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lms.IntegrationTests.Seeding;

/// <summary>
/// Boots the real application in the Development environment (DI validation on, no test service overrides),
/// the way `dotnet run` does: catches missing registrations and a broken startup seed that unit-style tests miss.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DevelopmentStartupTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Api_boots_in_Development_with_valid_DI_and_seeds_the_sample_clients()
    {
        var connectionString = await factory.CreateIsolatedDatabaseAsync();
        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Lms", connectionString);
            builder.UseSetting("Seed:RunOnStartup", "true");
        });

        var health = await host.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        await using var db = LmsDbContextFactory.Create(connectionString, TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Organizations.CountAsync());
    }

    [Fact]
    public async Task First_start_on_an_existing_M0_database_migrates_then_backfills_the_M1_permissions()
    {
        // A database as M0 left it: schema up to the last M0 migration (no Roles.DefaultsVersion), one built-in role
        // holding only M0 permissions.
        var connectionString = await factory.CreateEmptyDatabaseAsync();
        var orgId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await using (var old = LmsDbContextFactory.Create(connectionString, TestCurrentContext.PlatformAdmin()))
        {
            await old.GetService<IMigrator>().MigrateAsync("20261110200704_AddFiles");
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO Organizations (Id, NameAr, NameEn, Type, CreatedAt, IsDeleted) VALUES ({0}, 'م', 'M0 client', 0, NOW(6), 0)", orgId);
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO Roles (Id, OrganizationId, BuiltInKey, NameAr, NameEn, BaseKind, IsBuiltIn, CreatedAt, IsDeleted) " +
                "VALUES ({0}, {1}, 'OrgAdmin', 'م', 'Admin', 'Staff', 1, NOW(6), 0)", roleId, orgId);
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO RolePermissions (Id, OrganizationId, RoleId, Permission) VALUES ({0}, {1}, {2}, 'OrgUnits.View')",
                Guid.NewGuid(), orgId, roleId);
        }

        await using var host = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Lms", connectionString);
            builder.UseSetting("Seed:RunOnStartup", "true");
        });
        _ = host.CreateClient(); // starts the app: seed migrates the schema, then the reconcile runs

        await using var db = LmsDbContextFactory.Create(connectionString, TestCurrentContext.PlatformAdmin());
        var permissions = await db.RolePermissions.Where(p => p.RoleId == roleId).Select(p => p.Permission).ToListAsync();
        Assert.Contains(Lms.Shared.Authorization.Permissions.OrgUnits.View, permissions);
        Assert.Contains(Lms.Shared.Authorization.Permissions.LevelTypes.View, permissions);
        Assert.Equal(1, (await db.Roles.SingleAsync(r => r.Id == roleId)).DefaultsVersion);
    }
}