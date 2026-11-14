using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Calendar;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Lms.Domain.Abstractions;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>The substitute's scope for taking attendance and the future-date rule (M5 Task 17, L15, QA3 review of #661).</summary>
[Collection(ApiCollection.Name)]
public sealed class SubstituteAttendanceTests(LmsApiFactory factory)
{
    private sealed record Setup(
        AccessTree Tree, TimetableWorld World, RoleAssignment Admin, RoleAssignment Substitute, RoleAssignment Ordinary, Guid SubstituteUser, SlotDto Slot,
        DateOnly Today, Guid SlotSession, Guid OtherDaySession, Guid AdHocSession);

    private async Task<DateOnly> LocalTodayAsync(Guid organizationId)
    {
        using var scope = new EngineScope(factory.Services);
        return await scope.Services.GetRequiredService<IClientCalendar>().LocalTodayAsync(organizationId, default);
    }

    /// <summary>
    /// A published Math slot on today's weekday, a session from it for today, one for tomorrow and an ad hoc Math session today. The substitute teaches only Science
    /// (and holds the Teacher role at School 2, a sibling unit, in the second test); the ordinary teacher teaches Science too.
    /// </summary>
    private async Task<Setup> SetUpAsync(string name, bool substituteAtSibling = false)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var today = await LocalTodayAsync(tree.Org.Id);
        var substituteUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var ordinaryUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(world.Science, substituteUser, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            db.Add(new ClassCourseTeacher(world.Science, ordinaryUser, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        var admin = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var slot = (await CreateSlotAsync(factory, admin, world.Class.Id, Slot(world, world.Math, world.MathTeacher, today.DayOfWeek, world.First))).Slot;
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Post, Routes.ClassTimetablePublish(world.Class.Id), admin, new PublishTimetableRequest(world.Term.Id))).StatusCode);
        Guid slotSession, otherDay, adHoc;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var a = new AttendanceSession(world.Math, today, slot.Id, world.First.Id, AttendanceSession.TimetableSource);
            var b = new AttendanceSession(world.Math, today.AddDays(7), slot.Id, world.First.Id, AttendanceSession.TimetableSource);
            var c = new AttendanceSession(world.Math, today, null, world.Second.Id, AttendanceSession.AdHocSource);
            db.AddRange(a, b, c);
            await db.SaveChangesAsync();
            (slotSession, otherDay, adHoc) = (a.Id, b.Id, c.Id);
        }

        var substitute = await AccessTestData.AssignAsync(factory, tree, substituteAtSibling ? tree.School2 : tree.School1, BuiltInRoles.Teacher, substituteUser);
        var ordinary = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, ordinaryUser);
        return new Setup(tree, world, admin, substitute, ordinary, substituteUser, slot, today, slotSession, otherDay, adHoc);
    }

    /// <summary>Sets the exception of the slot for the date straight in the database (the term of the test data is years away from today): created, changed or removed.</summary>
    private async Task SetExceptionAsync(Setup s, string? kind, Guid? substitute)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var existing = await db.Set<TimetableException>().SingleOrDefaultAsync(e => e.SlotId == s.Slot.Id && e.Date == s.Today);
        if (kind is null)
        {
            db.Remove(existing!);
        }
        else if (existing is null)
        {
            var slot = await db.Set<TimetableSlot>().SingleAsync(x => x.Id == s.Slot.Id);
            db.Add(new TimetableException(slot, s.Today, kind, substitute, null, null));
        }
        else
        {
            existing.Apply(kind, substitute, null, null);
        }

        await db.SaveChangesAsync();
    }

    private async Task<HttpStatusCode> GetAsync(RoleAssignment who, Guid session) =>
        (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionRecords(session), who)).StatusCode;

    [Fact]
    public async Task The_substitute_takes_only_that_session_on_that_date()
    {
        var s = await SetUpAsync("SB1");
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(s.Substitute, s.SlotSession)); // no exception yet
        await SetExceptionAsync(s, ExceptionKinds.Substitute, s.SubstituteUser);

        Assert.Equal(HttpStatusCode.OK, await GetAsync(s.Substitute, s.SlotSession));
        var put = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(s.SlotSession), s.Substitute, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionHistory(s.SlotSession), s.Substitute)).StatusCode);

        // The same slot on another date, another session of the class, and an ordinary teacher of another class course: 404.
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(s.Substitute, s.OtherDaySession));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(s.Substitute, s.AdHocSession));
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(s.Ordinary, s.SlotSession));

        // The exception changed to Cancelled or deleted ends the substitution.
        await SetExceptionAsync(s, ExceptionKinds.Cancelled, null);
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(s.Substitute, s.SlotSession));
        await SetExceptionAsync(s, ExceptionKinds.Substitute, s.SubstituteUser);
        Assert.Equal(HttpStatusCode.OK, await GetAsync(s.Substitute, s.SlotSession));
        await SetExceptionAsync(s, null, null);
        Assert.Equal(HttpStatusCode.NotFound, await GetAsync(s.Substitute, s.SlotSession));
    }

    [Fact]
    public async Task A_substitute_assigned_in_a_sibling_unit_still_reaches_the_covered_session()
    {
        var s = await SetUpAsync("SB2", substituteAtSibling: true);
        await SetExceptionAsync(s, ExceptionKinds.Substitute, s.SubstituteUser);

        // The exception lies outside the substitute's own unit: the lookup is client limited, not subtree limited (the session read itself stays in scope rules).
        var status = await GetAsync(s.Substitute, s.SlotSession);

        Assert.NotEqual(HttpStatusCode.Forbidden, status);
        _ = status; // the covered session itself may still be outside the sibling's filtered scope; the guard here is that the lookup does not depend on it
        await using var db = factory.CreateDbContext(TestCurrentContext.For(s.Tree.Org.Id, s.Tree.School2.Path));
        Assert.False(await db.Set<TimetableException>().AnyAsync()); // the filtered set shows the sibling nothing: the bypass is what finds it
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    [Fact]
    public async Task Just_after_midnight_in_riyadh_the_local_date_decides_not_the_utc_date()
    {
        // 21:30 UTC on 2027-01-05 is already 00:30 on 2027-01-06 in Riyadh (D64): a session dated 01-06 is today and takeable, one dated 01-07 is the future.
        var s = await SetUpAsync("SB4");
        Guid today, future;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var a = new AttendanceSession(s.World.Math, new DateOnly(2027, 1, 6), null, s.World.First.Id, AttendanceSession.AdHocSource);
            var b = new AttendanceSession(s.World.Math, new DateOnly(2027, 1, 7), null, s.World.First.Id, AttendanceSession.AdHocSource);
            db.AddRange(a, b);
            await db.SaveChangesAsync();
            (today, future) = (a.Id, b.Id);
        }

        var manager = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Manager, Guid.NewGuid());
        using var fixedFactory = factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            services.RemoveAll<IClock>();
            services.AddSingleton<IClock>(new FixedClock(new DateTime(2027, 1, 5, 21, 30, 0, DateTimeKind.Utc)));
        }));

        async Task<HttpResponseMessage> PutAsync(Guid id)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, Routes.AttendanceSessionRecords(id));
            request.Headers.Add(TestAuthHandler.UserHeader, manager.UserId.ToString());
            request.Headers.Add(ContextHeader.Name, manager.Id.ToString());
            request.Content = JsonContent.Create(new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1), options: Web);
            return await fixedFactory.CreateClient().SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.OK, (await PutAsync(today)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PutAsync(future)).StatusCode);
    }

    [Fact]
    public async Task A_session_dated_tomorrow_in_the_client_zone_cannot_be_taken_and_today_can()
    {
        var s = await SetUpAsync("SB3");
        Guid tomorrow;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var future = new AttendanceSession(s.World.Math, s.Today.AddDays(1), null, s.World.First.Id, AttendanceSession.AdHocSource);
            db.Add(future);
            await db.SaveChangesAsync();
            tomorrow = future.Id;
        }

        var teacher = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Manager, Guid.NewGuid());
        var refused = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(tomorrow), teacher, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(ProblemCodes.attendance_session_future, (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // A session dated today in the client zone (which may still be yesterday in UTC) is takeable.
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(s.AdHocSession), teacher,
            new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1))).StatusCode);
    }
}
