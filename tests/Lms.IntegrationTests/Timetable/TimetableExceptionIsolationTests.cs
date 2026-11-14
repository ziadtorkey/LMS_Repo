using Lms.Domain.Organizations;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Timetable;

/// <summary>The exception table is isolated by client and by org-unit subtree (rule 1, M5 Task 15). Rows hang off a slot of a class at a school.</summary>
[Collection(ApiCollection.Name)]
public sealed class TimetableExceptionIsolationTests(LmsApiFactory factory)
{
    [Fact]
    public async Task TimetableException_is_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "TXIA");
        var b = await AccessTestData.SeedAsync(factory, "TXIB");
        var inSchool1 = await AddAsync(a.School1);
        var inSchool2 = await AddAsync(a.School2);
        var otherClient = await AddAsync(b.School1);

        Assert.Equal(Sorted(inSchool1, inSchool2), await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool1], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([inSchool2], await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), inSchool1, inSchool2, otherClient));
        Assert.Equal([otherClient], await VisibleAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), inSchool1, inSchool2, otherClient));
    }

    [Fact]
    public async Task A_deleted_exception_is_invisible_and_frees_its_key()
    {
        var a = await AccessTestData.SeedAsync(factory, "TXIC");
        var id = await AddAsync(a.School1);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<TimetableException>().SingleAsync(x => x.Id == id));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await VisibleAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), id));
    }

    private async Task<Guid> AddAsync(OrgUnit unit)
    {
        var world = await NewWorldAsync(factory, unit);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var slot = new TimetableSlot(world.Class, world.Math.Id, world.Term.Id, world.MathTeacher, DayOfWeek.Sunday, world.First.Id, null);
        slot.Publish();
        var exception = new TimetableException(slot, world.Term.StartDate.AddDays(3), TimetableException.CancelledKind, null, null, null);
        db.AddRange(slot, exception);
        await db.SaveChangesAsync();
        return exception.Id;
    }

    private async Task<List<Guid>> VisibleAsync(TestCurrentContext who, params Guid[] among)
    {
        await using var db = factory.CreateDbContext(who);
        var ids = await db.Set<TimetableException>().Select(x => x.Id).ToListAsync();
        return ids.Where(among.Contains).Order().ToList();
    }

    private static List<Guid> Sorted(params Guid[] ids) => ids.Order().ToList();
}
