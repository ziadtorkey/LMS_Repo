using Lms.Infrastructure.Persistence;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using Xunit;

namespace Lms.IntegrationTests.Persistence;

/// <summary>D47: the AddOrganizationShortCode migration fills existing clients from their root unit code, de-duplicating clashes.</summary>
[Collection(ApiCollection.Name)]
public sealed class ShortCodeBackfillTests(LmsApiFactory factory)
{
    [Fact]
    public async Task Backfill_uses_root_unit_code_and_numbers_clashes()
    {
        var cs = await factory.CreateEmptyDatabaseAsync();
        await using var db = LmsDbContextFactory.Create(cs, TestCurrentContext.PlatformAdmin());
        var migrator = db.GetService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_AddOrganizationShortCode", StringComparison.Ordinal));
        var previous = all[all.IndexOf(target) - 1];
        await migrator.MigrateAsync(previous);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        await using (var conn = new MySqlConnection(cs))
        {
            await conn.OpenAsync();
            await Exec(conn, "SET FOREIGN_KEY_CHECKS=0");
            await Org(conn, a, "2020-01-01", "ALPHA_School");   // lower-cased, '_' -> '-'
            await Org(conn, b, "2020-01-02", "alpha-school");   // clash with a -> '-2'
            await Org(conn, c, "2020-01-03", "alpha-school");   // clash -> '-3'
            await Org(conn, d, "2020-01-04", "X");              // too short -> fallback from the id
        }

        await migrator.MigrateAsync();

        await using var after = LmsDbContextFactory.Create(cs, TestCurrentContext.PlatformAdmin());
        var codes = await after.Organizations.ToDictionaryAsync(o => o.Id, o => o.ShortCode);
        Assert.Equal("alpha-school", codes[a]);
        Assert.Equal("alpha-school-2", codes[b]);
        Assert.Equal("alpha-school-3", codes[c]);
        Assert.Equal("c" + d.ToString("N")[..12], codes[d]);
        Assert.Equal(4, codes.Values.Distinct().Count());
        Assert.All(codes.Values, v => Assert.True(Lms.Domain.Organizations.Organization.IsValidShortCode(v)));

        await migrator.MigrateAsync(previous); // Down() works
        await using var down = new MySqlConnection(cs);
        await down.OpenAsync();
        await using var check = new MySqlCommand("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'Organizations' AND column_name = 'ShortCode'", down);
        Assert.Equal(0L, Convert.ToInt64(await check.ExecuteScalarAsync()));
    }

    private static async Task Org(MySqlConnection conn, Guid id, string created, string rootCode)
    {
        await Exec(conn, $"INSERT INTO Organizations (Id, NameAr, NameEn, Type, Status, CreatedAt, IsDeleted) VALUES ('{id}', 'ع', 'n', 0, 'Active', '{created}', 0)");
        await Exec(conn,
            $"INSERT INTO OrgUnits (Id, OrganizationId, ParentId, LevelTypeId, Code, NameAr, NameEn, Path, Depth, Status, IsCourseManagementLevel, IsLevelTransfer, CreatedAt, IsDeleted) " +
            $"VALUES ('{Guid.NewGuid()}', '{id}', NULL, '{Guid.NewGuid()}', '{rootCode}', 'ع', 'n', '/{Guid.NewGuid():N}/', 0, 0, 0, 0, '{created}', 0)");
    }

    private static async Task Exec(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
