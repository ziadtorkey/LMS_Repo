using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.AcademicYears;
using Lms.Domain.Configuration;
using Lms.Infrastructure.AcademicYears;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.Shared;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.AcademicYears;

/// <summary>M5 Task 5 (L8, D62): two or three terms per year from the client setting and a name template.</summary>
[Collection(ApiCollection.Name)]
public sealed class TermStructureTests(LmsApiFactory factory) : IDisposable
{
    private static readonly DateOnly Start = new(2033, 9, 1);

    private readonly List<OrgUnitTestHost> _hosts = [];

    public void Dispose() => _hosts.ForEach(h => h.Dispose());

    private async Task<(AccessTree Tree, Lms.Domain.Access.RoleAssignment Admin, AcademicYearDto Year)> SetupAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await CreateYearAsync(factory, admin, tree.Root, name, Start, Start.AddMonths(10).AddDays(-1));
        return (tree, admin, year);
    }

    private Task<HttpResponseMessage> PutAsync(Lms.Domain.Access.RoleAssignment admin, Guid yearId, int terms) =>
        SendAsync(factory, HttpMethod.Put, Routes.AcademicYearTermStructure(yearId), admin, new TermStructureDto(terms, []));

    private async Task<List<Term>> TermsAsync(Guid yearId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Term>().Where(t => t.AcademicYearId == yearId).OrderBy(t => t.Order).ToListAsync();
    }

    private async Task SetTermsPerYearAsync(AccessTree tree, string valueJson)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new OrgUnitSetting(tree.Org.Id, tree.Root.Id, tree.Root.Path, SettingKeys.AcademicYearsTermsPerYear.Name, valueJson, false));
        await db.SaveChangesAsync();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task New_year_uses_terms_per_year_setting()
    {
        var (tree, admin, year) = await SetupAsync("TS1");

        var byDefault = await (await SendAsync(factory, HttpMethod.Get, Routes.AcademicYearTermStructure(year.Id), admin)).Content.ReadFromJsonAsync<TermStructureDto>(Web);
        await SetTermsPerYearAsync(tree, "3");
        var three = await (await SendAsync(factory, HttpMethod.Get, Routes.AcademicYearTermStructure(year.Id), admin)).Content.ReadFromJsonAsync<TermStructureDto>(Web);

        Assert.Equal(2, byDefault!.TermsPerYear); // D62: two terms by default
        Assert.Equal([1, 2], byDefault.TermNames.Select(n => n.Order));
        Assert.Equal(3, three!.TermsPerYear);      // switchable per client
        Assert.Equal([1, 2, 3], three.TermNames.Select(n => n.Order));
        Assert.All(three.TermNames, n =>
        {
            Assert.False(string.IsNullOrWhiteSpace(n.NameAr));
            Assert.False(string.IsNullOrWhiteSpace(n.NameEn));
        });
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Switching_structure_on_a_planned_year_adds_template_terms(int count)
    {
        var (_, admin, year) = await SetupAsync("TS2-" + count);

        var response = await PutAsync(admin, year.Id, count);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var terms = await TermsAsync(year.Id);
        Assert.Equal(count, terms.Count);
        Assert.Equal(Enumerable.Range(1, count), terms.Select(t => t.Order));
        Assert.Equal(year.StartDate, terms[0].StartDate);                       // the terms cover the year, in order, without gaps
        Assert.Equal(year.EndDate, terms[^1].EndDate);
        for (var i = 1; i < terms.Count; i++)
        {
            Assert.Equal(terms[i - 1].EndDate.AddDays(1), terms[i].StartDate);
        }

        Assert.Equal(TermTemplate.NamesFor(count).Select(n => n.NameEn), terms.Select(t => t.NameEn));
        Assert.All(terms, t => Assert.False(string.IsNullOrWhiteSpace(t.NameAr)));
    }

    [Fact]
    public async Task Switching_never_deletes_or_renames_existing_terms()
    {
        var (_, admin, year) = await SetupAsync("TS3");
        var custom = new SaveTermRequest("فصل خاص", "My own term", Start, Start.AddMonths(3));
        (await SendAsync(factory, HttpMethod.Post, Routes.AcademicYearTerms(year.Id), admin, custom)).EnsureSuccessStatusCode();

        (await PutAsync(admin, year.Id, 3)).EnsureSuccessStatusCode();
        var three = await TermsAsync(year.Id);
        (await PutAsync(admin, year.Id, 2)).EnsureSuccessStatusCode(); // back to two: nothing is removed
        var after = await TermsAsync(year.Id);

        Assert.Equal(3, three.Count);
        Assert.Equal("My own term", three[0].NameEn);                       // not renamed, not moved
        Assert.Equal((Start, Start.AddMonths(3)), (three[0].StartDate, three[0].EndDate));
        Assert.Equal(Start.AddMonths(3).AddDays(1), three[1].StartDate);   // the new terms follow it
        Assert.Equal(["Second term", "Third term"], three.Skip(1).Select(t => t.NameEn));
        Assert.Equal(three.Select(t => t.Id), after.Select(t => t.Id));
    }

    [Fact]
    public async Task Switching_with_dependants_is_409_and_changes_nothing()
    {
        var host = new OrgUnitTestHost(factory, services => services.AddScoped<ITermDependants>(_ => new AlwaysDependants()));
        _hosts.Add(host);
        var (_, admin, year) = await SetupAsync("TS4");
        (await SendAsync(factory, HttpMethod.Post, Routes.AcademicYearTerms(year.Id), admin, new SaveTermRequest("فصل", "One", Start, Start.AddMonths(3)))).EnsureSuccessStatusCode();

        var response = await host.SendAsync(HttpMethod.Put, Routes.AcademicYearTermStructure(year.Id), admin, new TermStructureDto(3, []));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("terms_have_dependants", await CodeAsync(response));
        Assert.Single(await TermsAsync(year.Id));
    }

    private sealed class AlwaysDependants : ITermDependants
    {
        public Task<bool> AnyAsync(IReadOnlyCollection<Guid> termIds, CancellationToken ct) => Task.FromResult(true);
    }

    [Theory]
    [InlineData(AcademicYearStatus.Active, "year_active")]
    [InlineData(AcademicYearStatus.Closed, "year_closed")]
    public async Task Switching_an_active_or_closed_year_is_refused(AcademicYearStatus status, string code)
    {
        var tree = await AccessTestData.SeedAsync(factory, "TS5" + status);
        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var year = await SeedYearAsync(factory, tree.Root, Start, Start.AddMonths(9), status);

        var response = await PutAsync(admin, year.Id, 3);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
        Assert.Empty(await TermsAsync(year.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Only_two_or_three_terms_are_accepted(int count)
    {
        var (_, admin, year) = await SetupAsync("TS6-" + count);

        var response = await PutAsync(admin, year.Id, count);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await TermsAsync(year.Id));
    }

    [Fact]
    public async Task A_year_with_no_room_after_its_last_term_is_409_and_creates_nothing_new()
    {
        var (_, admin, year) = await SetupAsync("TS7");
        (await SendAsync(factory, HttpMethod.Post, Routes.AcademicYearTerms(year.Id), admin, new SaveTermRequest("فصل", "All year", year.StartDate, year.EndDate))).EnsureSuccessStatusCode();

        var response = await PutAsync(admin, year.Id, 2);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("term_outside_year", await CodeAsync(response));
        Assert.Single(await TermsAsync(year.Id));
    }

    [Fact]
    public async Task Another_client_cannot_read_or_change_the_structure()
    {
        var (_, _, year) = await SetupAsync("TS8A");
        var other = await AccessTestData.SeedAsync(factory, "TS8B");
        var otherAdmin = await AccessTestData.AssignAsync(factory, other, other.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.AcademicYearTermStructure(year.Id), otherAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(otherAdmin, year.Id, 3)).StatusCode);
        Assert.Empty(await TermsAsync(year.Id));
    }
}
