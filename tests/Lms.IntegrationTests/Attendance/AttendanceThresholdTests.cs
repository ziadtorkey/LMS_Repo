using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Attendance;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>The absence threshold (M5 Task 20, FR-EXC-04, D61): 5 absences in 30 days, a late counts as a half, an excused record never, one alert per crossing.</summary>
[Collection(ApiCollection.Name)]
public sealed class AttendanceThresholdTests(LmsApiFactory factory)
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed record Setup(AccessTree Tree, TimetableWorld World, RoleAssignment Counselor, RoleAssignment Teacher, Guid CounselorUser, Guid[] Students, DateOnly Today);

    private AttendanceThresholdRunner NewRunner() =>
        new(factory.Services.GetRequiredService<IConfiguration>(), factory.Services.GetRequiredService<IClock>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AttendanceThresholdRunner>.Instance);

    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var counselorUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        DateOnly today;
        using (var scope = new EngineScope(factory.Services))
        {
            today = await scope.Services.GetRequiredService<Lms.Domain.Calendar.IClientCalendar>().LocalTodayAsync(tree.Org.Id, default);
        }

        var students = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            students.Add((await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode())).Id);
        }

        // Six taken sessions on the six days before today. A: five absences. B: four absences and a late (4.5). C: four absences and two lates (5.0). D: six excused.
        string[][] marks =
        [
            [Absent, Absent, Absent, Absent, Absent, Present],
            [Absent, Absent, Absent, Absent, Late, Present],
            [Absent, Absent, Absent, Absent, Late, Late],
            [Excused, Excused, Excused, Excused, Excused, Excused],
        ];
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        for (var day = 0; day < 6; day++)
        {
            var session = new AttendanceSession(world.Math, today.AddDays(-day - 1), null, world.First.Id, AttendanceSession.AdHocSource);
            session.RecordSave(Guid.NewGuid(), DateTime.UtcNow);
            db.Add(session);
            for (var s = 0; s < 4; s++)
            {
                db.Add(new AttendanceRecord(session, students[s], marks[s][day], null));
            }
        }

        await db.SaveChangesAsync();
        return new Setup(tree, world,
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, counselorUser),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id),
            counselorUser, [.. students], today);
    }

    private const string Absent = "Absent", Late = "Late", Present = "Present", Excused = "Excused";

    private async Task<List<AttendanceAlert>> AlertsAsync(Setup s)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AttendanceAlert>().Where(a => s.Students.Contains(a.StudentProfileId)).OrderBy(a => a.AlertedAtUtc).ToListAsync();
    }

    [Fact]
    public async Task Five_absences_in_thirty_days_alert_and_a_late_counts_as_a_half_and_an_excused_never()
    {
        var s = await SetUpAsync("AL1");

        var created = await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);

        Assert.Equal(2, created.Count);
        var alerts = await AlertsAsync(s);
        Assert.Equal(new HashSet<Guid> { s.Students[0], s.Students[2] }, alerts.Select(x => x.StudentProfileId).ToHashSet());
        var a = alerts.Single(x => x.StudentProfileId == s.Students[0]);
        Assert.Equal((5, 0, 30, 5m, 5m), (a.Absences, a.Lates, a.WindowDays, a.Score, a.Threshold));
        var c = alerts.Single(x => x.StudentProfileId == s.Students[2]);
        Assert.Equal((4, 2, 5m), (c.Absences, c.Lates, c.Score));
        Assert.DoesNotContain(alerts, x => x.StudentProfileId == s.Students[1]); // 4.5 stays below
        Assert.DoesNotContain(alerts, x => x.StudentProfileId == s.Students[3]); // excused never counts
    }

    [Fact]
    public async Task The_alert_is_not_repeated_until_it_clears_and_crosses_again()
    {
        var s = await SetUpAsync("AL2");
        await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);

        Assert.Empty(await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default)); // the second run opens nothing
        Assert.Equal(2, (await AlertsAsync(s)).Count);

        await SetStatusAsync(s.Students[0], Present); // the count drops below the threshold: the alert clears
        await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);
        Assert.NotNull((await AlertsAsync(s)).Single(a => a.StudentProfileId == s.Students[0]).ClearedAtUtc);

        await SetStatusAsync(s.Students[0], Absent); // and crosses again: a new alert
        var again = await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);
        Assert.Single(again);
        Assert.Equal(2, (await AlertsAsync(s)).Count(a => a.StudentProfileId == s.Students[0]));
    }

    private async Task SetStatusAsync(Guid studentId, string status)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        foreach (var record in await db.Set<AttendanceRecord>().Where(r => r.StudentProfileId == studentId).ToListAsync())
        {
            record.Set(status, null);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Only_records_inside_the_window_count()
    {
        var s = await SetUpAsync("AL3");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            // A student with five absences, all 40 days ago, has no alert.
            var student = (await PeopleTestKit.AddStudentAsync(factory, s.Tree.School1, PeopleTestKit.NewCode())).Id;
            for (var i = 0; i < 5; i++)
            {
                var session = new AttendanceSession(s.World.Math, s.Today.AddDays(-40 - i), null, s.World.First.Id, AttendanceSession.AdHocSource);
                session.RecordSave(Guid.NewGuid(), DateTime.UtcNow);
                db.Add(session);
                db.Add(new AttendanceRecord(session, student, Absent, null));
            }

            await db.SaveChangesAsync();
            await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);
            Assert.False(await db.Set<AttendanceAlert>().AnyAsync(a => a.StudentProfileId == student));
        }
    }

    [Fact]
    public async Task The_counselor_is_notified_once_per_crossing_without_marks_and_reads_the_alert_list()
    {
        var s = await SetUpAsync("AL4");

        var created = await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);
        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Services.GetRequiredService<AttendanceThresholdJob>().NotifyAsync(created, default);
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var notices = await db.Set<Notification>().Where(n => n.UserId == s.CounselorUser && n.EventKey == NotificationEvents.AttendanceThresholdCrossed).ToListAsync();
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.DoesNotContain("Absent", n.ParametersJson)); // the count only, never the marks

        var list = await SendAsync(factory, HttpMethod.Get, Routes.AttendanceAlertsPattern, s.Counselor);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = (await list.Content.ReadFromJsonAsync<PagedResult<AttendanceAlertDto>>(Web))!;
        Assert.Equal(2, page.Items.Count(i => s.Students.Contains(i.StudentId)));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceAlertsPattern, s.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, $"{Routes.AttendanceAlertsPattern}?unitId={s.Tree.School2.Id}", s.Counselor)).StatusCode);
    }

    private async Task SetAsync<T>(Setup s, Lms.Shared.Configuration.SettingKey<T> key, T value)
    {
        using var scope = new EngineScope(factory.Services);
        await scope.Settings.SetAsync(key, s.Tree.Root.Id, value, false); // a client-wide override at the client root
    }

    private async Task<HashSet<Guid>> AlertedAsync(Setup s)
    {
        await NewRunner().RunForOrganizationAsync(s.Tree.Org.Id, default);
        return (await AlertsAsync(s)).Select(a => a.StudentProfileId).ToHashSet();
    }

    [Fact]
    public async Task The_threshold_is_a_per_client_setting()
    {
        var a = await SetUpAsync("AL6A");
        var b = await SetUpAsync("AL6B");
        await SetAsync(a, Lms.Shared.Configuration.SettingKeys.AttendanceAbsentThreshold, 4); // client A is stricter; client B keeps the default of 5

        Assert.Equal(new HashSet<Guid> { a.Students[0], a.Students[1], a.Students[2] }, await AlertedAsync(a)); // 5, 4.5 and 5.0 all reach 4
        Assert.Equal(new HashSet<Guid> { b.Students[0], b.Students[2] }, await AlertedAsync(b));
    }

    [Fact]
    public async Task The_late_weight_is_a_per_client_setting()
    {
        var a = await SetUpAsync("AL7A");
        var b = await SetUpAsync("AL7B");
        await SetAsync(a, Lms.Shared.Configuration.SettingKeys.AttendanceLateWeightPercent, 100); // a late is a full absence in client A

        Assert.Equal(new HashSet<Guid> { a.Students[0], a.Students[1], a.Students[2] }, await AlertedAsync(a)); // B: 4 + 1 = 5
        Assert.Equal(new HashSet<Guid> { b.Students[0], b.Students[2] }, await AlertedAsync(b));
    }

    [Fact]
    public async Task The_window_is_a_per_client_setting()
    {
        var a = await SetUpAsync("AL8A");
        var b = await SetUpAsync("AL8B");
        await SetAsync(a, Lms.Shared.Configuration.SettingKeys.AttendanceThresholdWindowDays, 3);

        Assert.Empty(await AlertedAsync(a)); // today and the two days before hold at most two of the six sessions: below 5
        await SetAsync(a, Lms.Shared.Configuration.SettingKeys.AttendanceAbsentThreshold, 2);
        Assert.Equal(new HashSet<Guid> { a.Students[0], a.Students[1], a.Students[2] }, await AlertedAsync(a)); // 2 absences in the 3 day window
        Assert.Equal(new HashSet<Guid> { b.Students[0], b.Students[2] }, await AlertedAsync(b)); // client B still looks 30 days back
    }

    [Fact]
    public async Task The_thirty_day_window_ends_at_the_client_local_date_not_the_utc_date()
    {
        // 21:30 UTC on 2027-01-05 is 00:30 on 2027-01-06 in Riyadh. The 30 day window is then 2026-12-08 .. 2027-01-06; counted in UTC it would be 2026-12-07 .. 2027-01-05.
        var tree = await AccessTestData.SeedAsync(factory, "AL9");
        var world = await NewWorldAsync(factory, tree.School1);
        var onEdge = (await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode())).Id;    // four recent absences and one on 12-08: inside
        var justOut = (await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode())).Id;   // four recent absences and one on 12-07: outside in Riyadh
        var tomorrow = (await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode())).Id;  // four recent absences and one dated 01-06 (the local today)
        DateOnly[] recent = [new(2027, 1, 2), new(2027, 1, 3), new(2027, 1, 4), new(2027, 1, 5)];
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            void Mark(DateOnly date, params Guid[] students)
            {
                var session = new AttendanceSession(world.Math, date, null, world.First.Id, AttendanceSession.AdHocSource);
                session.RecordSave(Guid.NewGuid(), DateTime.UtcNow);
                db.Add(session);
                foreach (var student in students)
                {
                    db.Add(new AttendanceRecord(session, student, Absent, null));
                }
            }

            foreach (var day in recent)
            {
                Mark(day, onEdge, justOut, tomorrow);
            }

            Mark(new DateOnly(2026, 12, 8), onEdge);
            Mark(new DateOnly(2026, 12, 7), justOut);
            Mark(new DateOnly(2027, 1, 6), tomorrow);
            await db.SaveChangesAsync();
        }

        var runner = new AttendanceThresholdRunner(factory.Services.GetRequiredService<IConfiguration>(), new FixedClock(new DateTime(2027, 1, 5, 21, 30, 0, DateTimeKind.Utc)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AttendanceThresholdRunner>.Instance);
        await runner.RunForOrganizationAsync(tree.Org.Id, default);

        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var alerted = (await read.Set<AttendanceAlert>().Where(a => new[] { onEdge, justOut, tomorrow }.Contains(a.StudentProfileId)).Select(a => a.StudentProfileId).ToListAsync()).ToHashSet();
        Assert.Contains(onEdge, alerted);       // 12-08 is the first day of the Riyadh window
        Assert.DoesNotContain(justOut, alerted); // 12-07 is outside it (it would be inside a UTC based window)
        Assert.Contains(tomorrow, alerted);     // 01-06 is today in Riyadh (it would be in the future for a UTC based "today")
    }

    [Fact]
    public async Task Another_client_alerts_are_never_created_or_listed()
    {
        var a = await SetUpAsync("AL5A");
        var b = await SetUpAsync("AL5B");
        await NewRunner().RunForOrganizationAsync(a.Tree.Org.Id, default);

        Assert.Empty(await AlertsAsync(b)); // client B was not run
        var list = await (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceAlertsPattern, b.Counselor)).Content.ReadFromJsonAsync<PagedResult<AttendanceAlertDto>>(Web);
        Assert.DoesNotContain(list!.Items, i => a.Students.Contains(i.StudentId));
    }
}
