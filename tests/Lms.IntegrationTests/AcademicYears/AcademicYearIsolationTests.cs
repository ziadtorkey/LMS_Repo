using System.Net;
using System.Net.Http.Json;
using Lms.Domain.AcademicYears;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.AcademicYears;

[Collection(ApiCollection.Name)]
public sealed class AcademicYearIsolationTests(LmsApiFactory factory)
{
    private static readonly DateOnly Y1 = new(2032, 9, 1);

    [Fact]
    public async Task School2_years_invisible_to_School1_and_client_B_invisible()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI1A");
        var b = await AccessTestData.SeedAsync(factory, "AI1B");
        var adminA = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school1Admin = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var adminB = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year2 = await CreateYearAsync(factory, adminA, a.School2, "S2", Y1, Y1.AddMonths(10));
        var term2 = await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{year2.Id}/terms", adminA,
            new SaveTermRequest("ف", "T", Y1, Y1.AddMonths(2)));
        term2.EnsureSuccessStatusCode();
        var term2Id = (await term2.Content.ReadFromJsonAsync<TermDto>(Web))!.Id;

        // School 1 admin: unit, year and term of School 2 do not exist for them.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, $"/api/academic-years/effective?orgUnitId={a.School2.Id}", school1Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, $"/api/academic-years/{year2.Id}", school1Admin, YearRequest(a.School2, "X", Y1, Y1.AddMonths(5)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{year2.Id}/activate", school1Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, $"/api/academic-years/{year2.Id}", school1Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, $"/api/terms/{term2Id}", school1Admin, new SaveTermRequest("ف", "T", Y1, Y1.AddMonths(1)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, "/api/academic-years", school1Admin, YearRequest(a.School2, "Y", Y1.AddYears(5), Y1.AddYears(6)))).StatusCode);

        // Client B: nothing of client A is reachable.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, $"/api/academic-years/effective?orgUnitId={a.School2.Id}", adminB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, $"/api/academic-years/{year2.Id}", adminB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, $"/api/terms/{term2Id}", adminB)).StatusCode);
        Assert.Empty((await EffectiveAsync(factory, adminB, b.Root)).Years);

        // The query filters themselves (both tables) hide rows outside the caller's scope.
        await using var db = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        Assert.False(await db.Set<AcademicYear>().AnyAsync(y => y.Id == year2.Id));
        Assert.False(await db.Set<Term>().AnyAsync(t => t.AcademicYearId == year2.Id));
        await using var dbB = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.False(await dbB.Set<AcademicYear>().AnyAsync(y => y.Id == year2.Id));
        Assert.False(await dbB.Set<Term>().AnyAsync(t => t.AcademicYearId == year2.Id));
    }

    [Fact]
    public async Task Another_clients_unit_name_is_never_resolved()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI4A");
        var b = await AccessTestData.SeedAsync(factory, "AI4B");
        var adminA = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, adminA, a.Root, "Forged", Y1, Y1.AddMonths(10));

        // Corrupt row: client A's year points at client B's unit.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE AcademicYears SET OrgUnitId = {b.Root.Id} WHERE Id = {year.Id}");
        }

        var ctx = await EffectiveAsync(factory, adminA, a.Root);

        var row = Assert.Single(ctx.Years);
        Assert.NotEqual(b.Root.NameEn, row.OrgUnitNameEn);
        Assert.Equal(string.Empty, row.OrgUnitNameEn);
    }

    [Fact]
    public async Task Year_changes_are_audited()
    {
        var a = await AccessTestData.SeedAsync(factory, "AI3");
        var admin = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, a.School1, "Aud", Y1, Y1.AddMonths(10));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var entry = await db.AuditEntries.SingleAsync(e => e.EntityId == year.Id);
        Assert.Equal("AcademicYear", entry.EntityType);
    }
}
