using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Timetable;

[Collection(ApiCollection.Name)]
public sealed class PeriodTests(LmsApiFactory factory)
{
    private static SavePeriodRequest Body(string name, int startHour, int endHour, int order) =>
        new("حصة " + name, "Period " + name, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), order);

    private static async Task<PeriodDto> CreateAsync(LmsApiFactory f, Lms.Domain.Access.RoleAssignment who, Guid unitId, SavePeriodRequest body)
    {
        var response = await SendAsync(f, HttpMethod.Post, Routes.OrgUnitPeriods(unitId), who, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PeriodDto>(Web))!;
    }

    private static async Task<List<PeriodDto>> ListAsync(LmsApiFactory f, Lms.Domain.Access.RoleAssignment who, Guid unitId)
    {
        var response = await SendAsync(f, HttpMethod.Get, Routes.OrgUnitPeriods(unitId), who);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<PeriodDto>>(Web))!;
    }

    [Fact]
    public async Task Periods_inherit_and_a_lower_unit_overrides()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PE1");
        var root = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var first = await CreateAsync(factory, root, tree.Root.Id, Body("1", 8, 9, 1));
        await CreateAsync(factory, root, tree.Root.Id, Body("2", 9, 10, 2));

        // The school has none of its own: it sees the root's set, read only.
        var inherited = await ListAsync(factory, school, tree.School1.Id);
        Assert.Equal(2, inherited.Count);
        Assert.All(inherited, p => Assert.Equal(tree.Root.Id, p.InheritedFromUnitId));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, Routes.Period(tree.School1.Id, first.Id), school, Body("1", 8, 9, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.Period(tree.School1.Id, first.Id), school)).StatusCode);

        // Its own period replaces the inherited set; a sibling still inherits.
        await CreateAsync(factory, school, tree.School1.Id, Body("A", 7, 8, 1));
        var own = await ListAsync(factory, school, tree.School1.Id);
        Assert.Single(own);
        Assert.Null(own[0].InheritedFromUnitId);
        Assert.Equal(tree.School1.Id, own[0].OrgUnitId);
        var sibling = await ListAsync(factory, root, tree.School2.Id);
        Assert.Equal(2, sibling.Count);
        Assert.All(sibling, p => Assert.Equal(tree.Root.Id, p.InheritedFromUnitId));
    }

    [Fact]
    public async Task Overlapping_periods_are_refused_but_touching_ones_are_fine()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PE2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var first = await CreateAsync(factory, admin, tree.Root.Id, Body("1", 8, 9, 1));

        var overlap = await SendAsync(factory, HttpMethod.Post, Routes.OrgUnitPeriods(tree.Root.Id), admin,
            new SavePeriodRequest("س", "Overlap", new TimeOnly(8, 30), new TimeOnly(9, 30), 2));
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Contains(ProblemCodes.period_overlap, await overlap.Content.ReadAsStringAsync());

        var touching = await CreateAsync(factory, admin, tree.Root.Id, Body("2", 9, 10, 2));
        var moveOntoFirst = await SendAsync(factory, HttpMethod.Put, Routes.Period(tree.Root.Id, touching.Id), admin, Body("2", 8, 10, 2));
        Assert.Equal(HttpStatusCode.Conflict, moveOntoFirst.StatusCode);

        // Editing a period against itself is not an overlap.
        var rename = await SendAsync(factory, HttpMethod.Put, Routes.Period(tree.Root.Id, first.Id), admin, Body("1b", 8, 9, 1));
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
    }

    [Fact]
    public async Task Invalid_periods_are_rejected_and_times_stay_local_wall_time()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PE3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Post, Routes.OrgUnitPeriods(tree.Root.Id), admin, Body("1", 9, 8, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Post, Routes.OrgUnitPeriods(tree.Root.Id), admin, Body("1", 8, 9, 21))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Post, Routes.OrgUnitPeriods(tree.Root.Id), admin, new SavePeriodRequest("", "x", new TimeOnly(8, 0), new TimeOnly(9, 0), 1))).StatusCode);

        var created = await CreateAsync(factory, admin, tree.Root.Id, new SavePeriodRequest("ص", "P", new TimeOnly(7, 45), new TimeOnly(8, 30), 1));
        var read = (await ListAsync(factory, admin, tree.Root.Id)).Single(p => p.Id == created.Id);
        Assert.Equal((new TimeOnly(7, 45), new TimeOnly(8, 30)), (read.StartTime, read.EndTime));
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.Period(tree.Root.Id, created.Id), admin)).StatusCode);
        Assert.Empty(await ListAsync(factory, admin, tree.Root.Id));
    }

    [Fact]
    public async Task Teacher_cannot_configure_periods_but_can_read_them()
    {
        var tree = await AccessTestData.SeedAsync(factory, "PE4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var period = await CreateAsync(factory, admin, tree.Root.Id, Body("1", 8, 9, 1));

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Post, Routes.OrgUnitPeriods(tree.School1.Id), teacher, Body("T", 10, 11, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Put, Routes.Period(tree.Root.Id, period.Id), teacher, Body("T", 8, 9, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Delete, Routes.Period(tree.Root.Id, period.Id), teacher)).StatusCode);
        Assert.Single(await ListAsync(factory, teacher, tree.School1.Id));
    }

    [Fact]
    public async Task Periods_never_cross_clients()
    {
        var a = await AccessTestData.SeedAsync(factory, "PE5A");
        var b = await AccessTestData.SeedAsync(factory, "PE5B");
        var adminA = await AccessTestData.AssignAsync(factory, a, a.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var adminB = await AccessTestData.AssignAsync(factory, b, b.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school1Admin = await AccessTestData.AssignAsync(factory, a, a.School1, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var period = await CreateAsync(factory, adminA, a.School2.Id, Body("1", 8, 9, 1));

        // Client B, and a sibling school's admin, cannot read or touch A's school 2 periods.
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.OrgUnitPeriods(a.School2.Id), adminB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Post, Routes.OrgUnitPeriods(a.School2.Id), adminB, Body("x", 10, 11, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.Period(a.School2.Id, period.Id), adminB)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.OrgUnitPeriods(a.School2.Id), school1Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Delete, Routes.Period(a.School2.Id, period.Id), school1Admin)).StatusCode);

        // A client without periods of its own never inherits another client's.
        Assert.Empty(await ListAsync(factory, adminB, b.School1.Id));

        // The table's query filter hides the row outside the caller's scope.
        await using var dbS1 = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School1.Path));
        Assert.False(await dbS1.Set<Period>().AnyAsync(p => p.Id == period.Id));
        await using var dbB = factory.CreateDbContext(TestCurrentContext.For(b.Org.Id, b.Root.Path));
        Assert.False(await dbB.Set<Period>().AnyAsync(p => p.Id == period.Id));
        await using var dbA = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.School2.Path));
        Assert.True(await dbA.Set<Period>().AnyAsync(p => p.Id == period.Id));
    }
}
