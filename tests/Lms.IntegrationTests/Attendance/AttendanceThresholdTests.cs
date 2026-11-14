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
