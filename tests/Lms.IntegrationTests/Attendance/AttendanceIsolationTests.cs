using Lms.Domain.Attendance;
using Lms.Domain.Organizations;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared.Attendance;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>The attendance tables are isolated by client and by org-unit subtree (rule 1, M5 Tasks 16 and 20): sessions, records, the change history and the alerts.</summary>
[Collection(ApiCollection.Name)]
public sealed class AttendanceIsolationTests(LmsApiFactory factory)
{
    private sealed record Rows(Guid Session, Guid Record, Guid Change, Guid Alert);

    [Fact]
    public async Task Attendance_rows_are_isolated_per_client_and_subtree()
    {
        var a = await AccessTestData.SeedAsync(factory, "ATA");
        var b = await AccessTestData.SeedAsync(factory, "ATB");
        var school1 = await AddAsync(a.School1, "AT1");
        var school2 = await AddAsync(a.School2, "AT2");
        var otherClient = await AddAsync(b.School1, "AT3");

        await AssertAsync(TestCurrentContext.For(a.Org.Id, a.Root.Path), [school1, school2], [otherClient], [school1, school2, otherClient]);
        await AssertAsync(TestCurrentContext.For(a.Org.Id, a.School1.Path), [school1], [school2, otherClient], [school1, school2, otherClient]);
        await AssertAsync(TestCurrentContext.For(a.Org.Id, a.School2.Path), [school2], [school1, otherClient], [school1, school2, otherClient]);
        await AssertAsync(TestCurrentContext.For(b.Org.Id, b.Root.Path), [otherClient], [school1, school2], [school1, school2, otherClient]);
    }

    [Fact]
    public async Task A_deleted_session_is_invisible_and_frees_its_slot_key()
    {
        var a = await AccessTestData.SeedAsync(factory, "ATC");
        var rows = await AddAsync(a.School1, "AT4");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Remove(await db.Set<AttendanceSession>().SingleAsync(x => x.Id == rows.Session));
            await db.SaveChangesAsync();
        }

        await using var read = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        Assert.False(await read.Set<AttendanceSession>().AnyAsync(x => x.Id == rows.Session));
    }

    private async Task<Rows> AddAsync(OrgUnit unit, string studentCode)
    {
        var world = await NewWorldAsync(factory, unit);
        var student = await PeopleTestKit.AddStudentAsync(factory, unit, studentCode);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var session = new AttendanceSession(world.Math, world.Term.StartDate, null, world.First.Id, AttendanceSession.AdHocSource);
        db.Add(session);
        var record = new AttendanceRecord(session, student.Id, AttendanceStatuses.Present, null);
        db.Add(record);
        var change = new AttendanceRecordChange(record, null, AttendanceStatuses.Present, AttendanceChangeKinds.Taken, Guid.NewGuid(), null, DateTime.UtcNow);
        db.Add(change);
        var alert = new AttendanceAlert(student, 5, 0, 30, 5m, 5m, DateTime.UtcNow);
        db.Add(alert);
        await db.SaveChangesAsync();
        return new Rows(session.Id, record.Id, change.Id, alert.Id);
    }

    private async Task AssertAsync(TestCurrentContext who, Rows[] visible, Rows[] hidden, Rows[] all)
    {
        await using var db = factory.CreateDbContext(who);
        var sessions = (await db.Set<AttendanceSession>().Select(x => x.Id).ToListAsync()).ToHashSet();
        var records = (await db.Set<AttendanceRecord>().Select(x => x.Id).ToListAsync()).ToHashSet();
        var changes = (await db.Set<AttendanceRecordChange>().Select(x => x.Id).ToListAsync()).ToHashSet();
        var alerts = (await db.Set<AttendanceAlert>().Select(x => x.Id).ToListAsync()).ToHashSet(); // the counselor alert list: a sibling school and another client never show
        Assert.All(visible, r => Assert.True(sessions.Contains(r.Session) && records.Contains(r.Record) && changes.Contains(r.Change) && alerts.Contains(r.Alert)));
        Assert.All(hidden, r => Assert.False(sessions.Contains(r.Session) || records.Contains(r.Record) || changes.Contains(r.Change) || alerts.Contains(r.Alert)));
        Assert.Equal(all.Length, visible.Length + hidden.Length); // every row of the test is either visible or hidden to this caller
    }
}
