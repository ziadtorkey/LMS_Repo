using Lms.Domain.Attendance;
using Lms.Domain.Excuses;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Excuses;

/// <summary>The excuse tables are isolated by client and by org-unit subtree (rule 1, M5 Task 19). Both live at the student's home unit.</summary>
[Collection(ApiCollection.Name)]
public sealed class ExcuseIsolationTests(LmsApiFactory factory)
{
    [Fact]
    public async Task AbsenceExcuse_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "EXA");
        var b = await AccessTestData.SeedAsync(factory, "EXB");
        var inSchool1 = (await AddAsync(a.School1, "EX1")).Excuse;
        var inSchool2 = (await AddAsync(a.School2, "EX2")).Excuse;
        var otherClient = (await AddAsync(b.School1, "EX3")).Excuse;

        Assert.Equal(Sorted(inSchool1, inSchool2), await VisibleExcusesAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool1], await VisibleExcusesAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool2], await VisibleExcusesAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleExcusesAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    [Fact]
    public async Task AbsenceExcuseSession_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "EXC");
        var b = await AccessTestData.SeedAsync(factory, "EXD");
        var inSchool1 = (await AddAsync(a.School1, "EX4")).Link;
        var inSchool2 = (await AddAsync(a.School2, "EX5")).Link;
        var otherClient = (await AddAsync(b.School1, "EX6")).Link;

        Assert.Equal(Sorted(inSchool1, inSchool2), await VisibleLinksAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool1], await VisibleLinksAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleLinksAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    [Fact]
    public async Task A_deleted_excuse_is_invisible()
    {
        var a = await AccessTestData.SeedAsync(factory, "EXE");
        var excuse = (await AddAsync(a.School1, "EX7")).Excuse;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<AbsenceExcuse>().SingleAsync(x => x.Id == excuse));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await VisibleExcusesAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), excuse));
    }

    private async Task<(Guid Excuse, Guid Link)> AddAsync(OrgUnit home, string studentCode)
    {
        var student = await PeopleTestKit.AddStudentAsync(factory, home, studentCode);
        var world = await Timetable.TimetableKit.NewWorldAsync(factory, home);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var session = new AttendanceSession(world.Math, new DateOnly(2027, 2, 4), null, null, AttendanceSession.AdHocSource);
        db.Add(session);
        var excuse = new AbsenceExcuse(student, Guid.NewGuid(), "Illness", null, new DateOnly(2027, 2, 4), new DateOnly(2027, 2, 4), null, DateTime.UtcNow);
        var link = new AbsenceExcuseSession(excuse, session.Id);
        db.AddRange(excuse, link);
        await db.SaveChangesAsync();
        return (excuse.Id, link.Id);
    }

    private async Task<List<Guid>> VisibleExcusesAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<AbsenceExcuse>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private async Task<List<Guid>> VisibleLinksAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<AbsenceExcuseSession>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();
}
