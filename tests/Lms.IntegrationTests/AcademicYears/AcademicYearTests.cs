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
public sealed class AcademicYearTests(LmsApiFactory factory)
{
    private static readonly DateOnly Y1 = new(2030, 9, 1);

    [Fact]
    public async Task Root_year_is_inherited_by_school_and_marked_IsInherited()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY1");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var created = await CreateYearAsync(factory, admin, tree.Root, "2030", Y1, Y1.AddMonths(10));

        var atRoot = await EffectiveAsync(factory, admin, tree.Root);
        var atSchool = await EffectiveAsync(factory, admin, tree.School1);

        var rootYear = Assert.Single(atRoot.Years);
        Assert.False(rootYear.IsInherited);
        Assert.Equal(tree.Root.Id, atRoot.DefiningOrgUnitId);
        var schoolYear = Assert.Single(atSchool.Years);
        Assert.Equal(created.Id, schoolYear.Id);
        Assert.True(schoolYear.IsInherited);
        Assert.Equal(tree.Root.Id, atSchool.DefiningOrgUnitId);
        Assert.Equal(tree.Root.NameEn, schoolYear.OrgUnitNameEn);
        Assert.Equal("عام 2030", schoolYear.NameAr);
    }

    [Fact]
    public async Task Faculty_with_own_years_overrides_university_years()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY2");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var rootYear = await CreateYearAsync(factory, admin, tree.Root, "Univ", Y1, Y1.AddMonths(10));
        var own = await CreateYearAsync(factory, admin, tree.School1, "Faculty", Y1, Y1.AddMonths(9));

        var atSchool = await EffectiveAsync(factory, admin, tree.School1);
        var atSchool2 = await EffectiveAsync(factory, admin, tree.School2);

        Assert.Equal(new[] { own.Id }, atSchool.Years.Select(y => y.Id));
        Assert.False(atSchool.Years[0].IsInherited);
        Assert.Equal(tree.School1.Id, atSchool.DefiningOrgUnitId);
        Assert.Equal(new[] { rootYear.Id }, atSchool2.Years.Select(y => y.Id));
    }

    [Fact]
    public async Task Only_one_active_year_per_unit()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var a = await CreateYearAsync(factory, admin, tree.Root, "A", Y1, Y1.AddMonths(10));
        var b = await CreateYearAsync(factory, admin, tree.Root, "B", Y1.AddYears(1), Y1.AddYears(1).AddMonths(10));

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{a.Id}/activate", admin)).StatusCode);
        var second = await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{b.Id}/activate", admin);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("active_year_exists", await second.Content.ReadAsStringAsync());
        var ctx = await EffectiveAsync(factory, admin, tree.Root);
        Assert.Equal(a.Id, ctx.CurrentYearId);
    }

    [Fact]
    public async Task Overlapping_years_at_same_unit_409_but_other_unit_is_fine()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY4");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        await CreateYearAsync(factory, admin, tree.Root, "A", Y1, Y1.AddMonths(10));

        var overlap = await SendAsync(factory, HttpMethod.Post, "/api/academic-years", admin, YearRequest(tree.Root, "B", Y1.AddMonths(5), Y1.AddMonths(15)));
        var elsewhere = await SendAsync(factory, HttpMethod.Post, "/api/academic-years", admin, YearRequest(tree.School1, "B", Y1.AddMonths(5), Y1.AddMonths(15)));

        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Contains("year_overlap", await overlap.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task Invalid_request_is_400()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY5");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var reversed = await SendAsync(factory, HttpMethod.Post, "/api/academic-years", admin, YearRequest(tree.Root, "X", Y1.AddDays(5), Y1));
        var noName = await SendAsync(factory, HttpMethod.Post, "/api/academic-years", admin, new SaveAcademicYearRequest(tree.Root.Id, "", "x", Y1, Y1.AddDays(5)));

        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
    }

    [Fact]
    public async Task Update_changes_planned_year_and_rejects_dates_that_orphan_terms()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY6");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "A", Y1, Y1.AddMonths(10));
        var term = await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{year.Id}/terms", admin,
            new SaveTermRequest("فصل", "T1", Y1.AddMonths(8), Y1.AddMonths(9)));
        term.EnsureSuccessStatusCode();

        var ok = await SendAsync(factory, HttpMethod.Put, $"/api/academic-years/{year.Id}", admin,
            YearRequest(tree.Root, "Renamed", Y1, Y1.AddMonths(11)));
        var orphan = await SendAsync(factory, HttpMethod.Put, $"/api/academic-years/{year.Id}", admin,
            YearRequest(tree.Root, "Renamed", Y1, Y1.AddMonths(5)));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("Year Renamed", (await ok.Content.ReadFromJsonAsync<AcademicYearDto>(Web))!.NameEn);
        Assert.Equal(HttpStatusCode.Conflict, orphan.StatusCode);
        Assert.Contains("term_outside_year", await orphan.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Closed_year_is_read_only()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY7");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var closed = await SeedYearAsync(factory, tree.Root, Y1.AddYears(-5), Y1.AddYears(-4), AcademicYearStatus.Closed);

        var update = await SendAsync(factory, HttpMethod.Put, $"/api/academic-years/{closed.Id}", admin, YearRequest(tree.Root, "X", Y1.AddYears(-5), Y1.AddYears(-4)));
        var activate = await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{closed.Id}/activate", admin);
        var delete = await SendAsync(factory, HttpMethod.Delete, $"/api/academic-years/{closed.Id}", admin);
        var addTerm = await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{closed.Id}/terms", admin,
            new SaveTermRequest("ف", "T", Y1.AddYears(-5), Y1.AddYears(-5).AddMonths(2)));

        foreach (var response in new[] { update, activate, delete, addTerm })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("year_closed", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Delete_active_year_409_and_planned_year_is_soft_deleted_with_its_terms()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY8");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var active = await CreateYearAsync(factory, admin, tree.Root, "Act", Y1, Y1.AddMonths(10));
        await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{active.Id}/activate", admin);
        var planned = await CreateYearAsync(factory, admin, tree.Root, "Plan", Y1.AddYears(1), Y1.AddYears(1).AddMonths(10));
        (await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{planned.Id}/terms", admin,
            new SaveTermRequest("ف", "T", Y1.AddYears(1), Y1.AddYears(1).AddMonths(3)))).EnsureSuccessStatusCode();

        var denied = await SendAsync(factory, HttpMethod.Delete, $"/api/academic-years/{active.Id}", admin);
        var deleted = await SendAsync(factory, HttpMethod.Delete, $"/api/academic-years/{planned.Id}", admin);

        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Contains("year_active", await denied.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var ctx = await EffectiveAsync(factory, admin, tree.Root);
        Assert.Equal(new[] { active.Id }, ctx.Years.Select(y => y.Id));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.True(await db.AcademicYears.IgnoreQueryFilters().AnyAsync(y => y.Id == planned.Id && y.IsDeleted));
        Assert.True(await db.Terms.IgnoreQueryFilters().AnyAsync(t => t.AcademicYearId == planned.Id && t.IsDeleted));
    }

    [Fact]
    public async Task Teacher_can_view_effective_years_but_not_create()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY9");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "A", Y1, Y1.AddMonths(10));

        var view = await EffectiveAsync(factory, teacher, tree.School1);
        var create = await SendAsync(factory, HttpMethod.Post, "/api/academic-years", teacher, YearRequest(tree.School1, "B", Y1.AddYears(3), Y1.AddYears(4)));
        var activate = await SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{year.Id}/activate", teacher);

        Assert.Equal(new[] { year.Id }, view.Years.Select(y => y.Id));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, activate.StatusCode);
    }

    [Fact]
    public async Task Effective_defaults_to_the_context_unit()
    {
        var tree = await AccessTestData.SeedAsync(factory, "AY10");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var school = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, "A", Y1, Y1.AddMonths(10));

        var response = await SendAsync(factory, HttpMethod.Get, "/api/academic-years/effective", school);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ctx = (await response.Content.ReadFromJsonAsync<YearContextDto>(Web))!;
        Assert.Equal(year.Id, ctx.Years.Single().Id);
    }
}
