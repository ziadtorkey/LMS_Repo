using System.Net;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared.Authorization;
using Lms.Shared;
using Lms.Shared.Organizations;
using Lms.Infrastructure.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.OrgUnits;

/// <summary>Moving a unit must carry the rows of the later modules (years, terms, calendar) with it (Review Focus 3).</summary>
[Collection(ApiCollection.Name)]
public sealed class MoveRepathsAcademicTablesTests(LmsApiFactory factory) : IDisposable
{
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Move_repaths_academic_years_terms_and_calendar_events()
    {
        var tree = await AccessTestData.SeedAsync(factory, "MVA1");
        RoleAssignment rootAdmin;
        OrgUnit section;
        AcademicYear year;
        Term term;
        CalendarEvent holiday;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var deeper = new LevelType(tree.Org.Id, "أعمق", "Deeper", 2, true);
            section = OrgUnit.CreateChild(tree.School1, deeper.Id, "قسم", "Section", "SEC");
            year = new AcademicYear(section, "عام", "Year", new DateOnly(2041, 9, 1), new DateOnly(2042, 6, 30));
            term = new Term(year, "فصل", "Term", new DateOnly(2041, 9, 1), new DateOnly(2041, 12, 31));
            holiday = new CalendarEvent(year, section, "عطلة", "Holiday", CalendarEventKind.Holiday, new DateOnly(2041, 10, 1), new DateOnly(2041, 10, 2));
            db.AddRange(deeper, section, year, term, holiday);
            await db.SaveChangesAsync();
        }

        rootAdmin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());

        var response = await _host.SendAsync(HttpMethod.Put, Routes.OrgUnitMove(section.Id), rootAdmin, new MoveOrgUnitRequest(tree.School2.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newPath = OrgUnitPath.Append(tree.School2.Path, section.Id);
        await using var check = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(newPath, (await check.Set<AcademicYear>().SingleAsync(x => x.Id == year.Id)).OrgUnitPath);
        Assert.Equal(newPath, (await check.Set<Term>().SingleAsync(x => x.Id == term.Id)).OrgUnitPath);
        Assert.Equal(newPath, (await check.Set<CalendarEvent>().SingleAsync(x => x.Id == holiday.Id)).OrgUnitPath);
    }

    [Fact]
    public void Coverage_names_the_academic_tables_and_every_other_unit_scoped_table()
    {
        using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        var covered = OrgUnitRepather.DiscoverTargets(db.Model).Select(t => t.ClrType.Name).ToHashSet();

        // The tables known today; anything added later is caught by the generic coverage test, this list documents them.
        foreach (var expected in new[]
                 {
                     nameof(OrgUnit), nameof(RoleAssignment), nameof(AcademicYear), nameof(Term), nameof(CalendarEvent),
                     "OrgUnitSetting", "OrgUnitPolicy", "StoredFile",
                 })
        {
            Assert.Contains(expected, covered);
        }
    }
}
