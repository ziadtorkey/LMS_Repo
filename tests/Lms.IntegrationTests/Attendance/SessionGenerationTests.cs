using Lms.Domain.Abstractions;
using Lms.Domain.AcademicYears;
using Lms.Domain.Attendance;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Attendance;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.Timetable;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>The generator turns published slots into attendance sessions for the next days (M5 Task 16, FR-TT-06, FR-ATT-01).</summary>
[Collection(ApiCollection.Name)]
public sealed class SessionGenerationTests(LmsApiFactory factory)
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed record Setup(TimetableWorld World, TimetableSlot Slot, DateOnly FirstSunday, AttendanceSessionGenerator Generator);

    private AttendanceSessionGenerator GeneratorAt(DateOnly localDay) =>
        new(factory.Services.GetRequiredService<IConfiguration>(), new FixedClock(localDay.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)));

    /// <summary>A Sunday first-period slot, published; "today" is the first Sunday of the term (the default 7 days ahead reach the next Sunday).</summary>
    private async Task<Setup> SetUpAsync(string name, DayOfWeek day = DayOfWeek.Sunday, bool publish = true)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var firstSunday = world.Term.StartDate;
        while (firstSunday.DayOfWeek != DayOfWeek.Sunday)
        {
            firstSunday = firstSunday.AddDays(1);
        }

        var slot = await AddSlotAsync(world, day, world.First, publish);
        return new Setup(world, slot, firstSunday, GeneratorAt(firstSunday));
    }

    private async Task<TimetableSlot> AddSlotAsync(TimetableWorld world, DayOfWeek day, Lms.Domain.Timetable.Period period, bool publish = true)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var slot = new TimetableSlot(world.Class, world.Math.Id, world.Term.Id, world.MathTeacher, day, period.Id, null);
        if (publish)
        {
            slot.Publish();
        }

        db.Add(slot);
        await db.SaveChangesAsync();
        return slot;
    }

    private async Task<List<AttendanceSession>> SessionsAsync(Guid slotId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AttendanceSession>().Where(s => s.SlotId == slotId).OrderBy(s => s.Date).ToListAsync();
    }

    [Fact]
    public async Task Generator_creates_sessions_for_the_next_days_only()
    {
        var s = await SetUpAsync("AG1");

        var created = await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);

        Assert.True(created >= 2);
        var sessions = await SessionsAsync(s.Slot.Id);
        Assert.Equal([s.FirstSunday, s.FirstSunday.AddDays(7)], sessions.Select(x => x.Date)); // today and the Sunday inside the 7 days; none before or after
        Assert.All(sessions, x => Assert.Equal((AttendanceSession.TimetableSource, AttendanceSession.OpenState, s.World.First.Id), (x.Source, x.State, x.PeriodId)));
        Assert.All(sessions, x => Assert.Equal((s.World.Math.Id, s.World.Class.Id, s.World.Unit.Id), (x.ClassCourseId, x.ClassId, x.OrgUnitId)));
    }

    [Fact]
    public async Task Generator_never_writes_a_session_before_today_and_skips_draft_slots()
    {
        var s = await SetUpAsync("AG2");
        var draft = await AddSlotAsync(s.World, DayOfWeek.Monday, s.World.Second, publish: false);

        await GeneratorAt(s.FirstSunday.AddDays(3)).GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default); // today is a Wednesday

        Assert.Equal([s.FirstSunday.AddDays(7)], (await SessionsAsync(s.Slot.Id)).Select(x => x.Date)); // the Sunday of the past week is not back-filled
        Assert.Empty(await SessionsAsync(draft.Id));
    }

    [Fact]
    public async Task Generator_skips_weekends_and_holidays()
    {
        var s = await SetUpAsync("AG3");
        var friday = await AddSlotAsync(s.World, DayOfWeek.Friday, s.World.Second); // Friday is not a working day
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var holiday = s.FirstSunday.AddDays(7);
            db.Set<CalendarEvent>().Add(new CalendarEvent(s.World.Year, s.World.Unit, "عطلة", "Holiday", CalendarEventKind.Holiday, holiday, holiday));
            await db.SaveChangesAsync();
        }

        await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);

        Assert.Equal([s.FirstSunday], (await SessionsAsync(s.Slot.Id)).Select(x => x.Date)); // the second Sunday is a holiday
        Assert.Empty(await SessionsAsync(friday.Id));
    }

    [Fact]
    public async Task Generator_applies_exceptions()
    {
        var s = await SetUpAsync("AG4");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var slot = await db.Set<TimetableSlot>().SingleAsync(x => x.Id == s.Slot.Id);
            db.Add(new TimetableException(slot, s.FirstSunday, TimetableException.CancelledKind, null, null, null));
            db.Add(new TimetableException(slot, s.FirstSunday.AddDays(7), TimetableException.MovedKind, null, s.World.Second.Id, null));
            await db.SaveChangesAsync();
        }

        await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);

        var sessions = await SessionsAsync(s.Slot.Id);
        Assert.Equal([s.FirstSunday.AddDays(7)], sessions.Select(x => x.Date)); // the cancelled date has none
        Assert.Equal(s.World.Second.Id, sessions.Single().PeriodId); // the moved one is in the new period
    }

    [Fact]
    public async Task Generator_is_idempotent()
    {
        var s = await SetUpAsync("AG5");

        var first = await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);
        var again = await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);

        Assert.True(first >= 2);
        Assert.Equal(0, again);
        Assert.Equal(2, (await SessionsAsync(s.Slot.Id)).Count);
    }

    [Fact]
    public async Task Generator_for_client_a_leaves_client_b_untouched()
    {
        var a = await SetUpAsync("AG6A");
        var b = await SetUpAsync("AG6B");

        await a.Generator.GenerateForOrganizationAsync(a.World.Unit.OrganizationId, null, null, default);

        Assert.Equal(2, (await SessionsAsync(a.Slot.Id)).Count);
        Assert.Empty(await SessionsAsync(b.Slot.Id));
    }

    [Fact]
    public async Task Slot_published_late_backfills_from_today()
    {
        var s = await SetUpAsync("AG7", publish: false);
        await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);
        Assert.Empty(await SessionsAsync(s.Slot.Id));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<TimetableSlot>().SingleAsync(x => x.Id == s.Slot.Id)).Publish();
            await db.SaveChangesAsync();
        }

        await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, s.World.Class.Id, s.World.Term.Id, default); // the run a publish queues

        Assert.Equal([s.FirstSunday, s.FirstSunday.AddDays(7)], (await SessionsAsync(s.Slot.Id)).Select(x => x.Date));
    }

    [Fact]
    public async Task A_closed_class_gets_no_new_sessions()
    {
        var s = await SetUpAsync("AG8");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Lms.Domain.Classes.Class>().SingleAsync(c => c.Id == s.World.Class.Id)).SetStatus(Lms.Domain.Classes.Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        await s.Generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);

        Assert.Empty(await SessionsAsync(s.Slot.Id));
    }
}
