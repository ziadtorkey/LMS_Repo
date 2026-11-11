using System.Net;
using System.Net.Http.Json;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.AcademicYears;

[Collection(ApiCollection.Name)]
public sealed class TermTests(LmsApiFactory factory)
{
    private static readonly DateOnly Y1 = new(2031, 9, 1);

    private async Task<(AccessTree Tree, Lms.Domain.Access.RoleAssignment Admin, AcademicYearDto Year)> SetupAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, name, Y1, Y1.AddMonths(10));
        return (tree, admin, year);
    }

    private Task<HttpResponseMessage> AddTermAsync(Lms.Domain.Access.RoleAssignment admin, Guid yearId, string name, DateOnly start, DateOnly end) =>
        SendAsync(factory, HttpMethod.Post, $"/api/academic-years/{yearId}/terms", admin, new SaveTermRequest("فصل " + name, "Term " + name, start, end));

    [Fact]
    public async Task Term_outside_year_409_and_overlapping_term_409()
    {
        var (_, admin, year) = await SetupAsync("TM1");
        (await AddTermAsync(admin, year.Id, "1", Y1, Y1.AddMonths(3))).EnsureSuccessStatusCode();

        var outside = await AddTermAsync(admin, year.Id, "x", Y1.AddMonths(-1), Y1.AddMonths(1));
        var overlap = await AddTermAsync(admin, year.Id, "y", Y1.AddMonths(2), Y1.AddMonths(5));
        var reversed = await AddTermAsync(admin, year.Id, "z", Y1.AddMonths(6), Y1.AddMonths(5));

        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);
        Assert.Contains("term_outside_year", await outside.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Contains("term_overlap", await overlap.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);
    }

    [Fact]
    public async Task Terms_are_ordered_by_start_date_and_reordered_on_change_and_delete()
    {
        var (tree, admin, year) = await SetupAsync("TM2");
        var late = (await (await AddTermAsync(admin, year.Id, "late", Y1.AddMonths(6), Y1.AddMonths(8))).Content.ReadFromJsonAsync<TermDto>(Web))!;
        var early = (await (await AddTermAsync(admin, year.Id, "early", Y1, Y1.AddMonths(2))).Content.ReadFromJsonAsync<TermDto>(Web))!;
        var mid = (await (await AddTermAsync(admin, year.Id, "mid", Y1.AddMonths(3), Y1.AddMonths(5))).Content.ReadFromJsonAsync<TermDto>(Web))!;

        var terms = (await EffectiveAsync(factory, admin, tree.Root)).Years.Single().Terms;
        Assert.Equal(new[] { early.Id, mid.Id, late.Id }, terms.Select(t => t.Id));
        Assert.Equal(new[] { 1, 2, 3 }, terms.Select(t => t.Order));

        // Move "early" after "late": order follows start dates again.
        var moved = await SendAsync(factory, HttpMethod.Put, $"/api/terms/{early.Id}", admin,
            new SaveTermRequest("ف", "early", Y1.AddMonths(8).AddDays(5), Y1.AddMonths(9)));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        terms = (await EffectiveAsync(factory, admin, tree.Root)).Years.Single().Terms;
        Assert.Equal(new[] { mid.Id, late.Id, early.Id }, terms.Select(t => t.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, $"/api/terms/{mid.Id}", admin)).StatusCode);
        terms = (await EffectiveAsync(factory, admin, tree.Root)).Years.Single().Terms;
        Assert.Equal(new[] { late.Id, early.Id }, terms.Select(t => t.Id));
        Assert.Equal(new[] { 1, 2 }, terms.Select(t => t.Order));
    }

    [Fact]
    public async Task Current_term_follows_the_clock()
    {
        var tree = await AccessTestData.SeedAsync(factory, "TM3");
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var start = Today.AddMonths(-1);
        var year = await CreateYearAsync(factory, admin, tree.Root, "Now", start.AddMonths(-3), start.AddMonths(12));
        (await AddTermAsync(admin, year.Id, "past", start.AddMonths(-3), start.AddMonths(-2))).EnsureSuccessStatusCode();
        (await AddTermAsync(admin, year.Id, "now", start, start.AddMonths(2))).EnsureSuccessStatusCode();
        (await AddTermAsync(admin, year.Id, "future", start.AddMonths(4), start.AddMonths(6))).EnsureSuccessStatusCode();

        var terms = (await EffectiveAsync(factory, admin, tree.Root)).Years.Single().Terms;

        Assert.Equal(new[] { false, true, false }, terms.Select(t => t.IsCurrent));
    }

    [Fact]
    public async Task Term_update_cannot_overlap_a_sibling_but_may_overlap_itself()
    {
        var (_, admin, year) = await SetupAsync("TM4");
        var a = (await (await AddTermAsync(admin, year.Id, "a", Y1, Y1.AddMonths(2))).Content.ReadFromJsonAsync<TermDto>(Web))!;
        (await AddTermAsync(admin, year.Id, "b", Y1.AddMonths(3), Y1.AddMonths(5))).EnsureSuccessStatusCode();

        var self = await SendAsync(factory, HttpMethod.Put, $"/api/terms/{a.Id}", admin, new SaveTermRequest("ف", "a2", Y1, Y1.AddMonths(2).AddDays(10)));
        var clash = await SendAsync(factory, HttpMethod.Put, $"/api/terms/{a.Id}", admin, new SaveTermRequest("ف", "a3", Y1, Y1.AddMonths(4)));

        Assert.Equal(HttpStatusCode.OK, self.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_edit_terms()
    {
        var (tree, admin, year) = await SetupAsync("TM5");
        var term = (await (await AddTermAsync(admin, year.Id, "a", Y1, Y1.AddMonths(2))).Content.ReadFromJsonAsync<TermDto>(Web))!;
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Teacher, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await AddTermAsync(teacher, year.Id, "b", Y1.AddMonths(3), Y1.AddMonths(4))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Delete, $"/api/terms/{term.Id}", teacher)).StatusCode);
    }
}
