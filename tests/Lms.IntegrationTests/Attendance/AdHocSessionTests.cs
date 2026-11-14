using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Attendance;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>Ad hoc sessions of a ClassCourse and the teacher day list with substitute sessions (M5 Task 16, FR-ATT-01, L15).</summary>
[Collection(ApiCollection.Name)]
public sealed class AdHocSessionTests(LmsApiFactory factory)
{
    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed record Setup(AccessTree Tree, TimetableWorld World, RoleAssignment Teacher, RoleAssignment OtherTeacher, RoleAssignment Counselor);

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static DateOnly LocalToday => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // the client time zone is Asia/Riyadh (D64)

    /// <summary>The latest school day (Sunday to Thursday, no holiday in the test data) on or before the local today: ad hoc sessions are for school days.</summary>
    private static DateOnly Today
    {
        get
        {
            var d = LocalToday;
            while (d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            {
                d = d.AddDays(-1);
            }

            return d;
        }
    }

    /// <summary>The Math class course is taught by a teacher whose assignment is active today; the Science one by another.</summary>
    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var mathTeacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var scienceTeacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(world.Math, mathTeacher, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            db.Add(new ClassCourseTeacher(world.Science, scienceTeacher, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        return new Setup(tree, world,
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, mathTeacher),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, scienceTeacher),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id));
    }

    private Task<HttpResponseMessage> CreateAsync(RoleAssignment who, Guid classCourseId, DateOnly date, Guid? period = null) =>
        SendAsync(factory, HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", classCourseId.ToString()), who,
            new CreateAdHocAttendanceSessionRequest(date, period));

    [Fact]
    public async Task Ad_hoc_session_for_own_class_course_is_idempotent_and_never_in_the_future()
    {
        var s = await SetUpAsync("AH1");

        var created = await CreateAsync(s.Teacher, s.World.Math.Id, Today, s.World.First.Id);
        var again = await CreateAsync(s.Teacher, s.World.Math.Id, Today, s.World.First.Id);
        var future = await CreateAsync(s.Teacher, s.World.Math.Id, LocalToday.AddDays(1));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = (await created.Content.ReadFromJsonAsync<AttendanceSessionListItemDto>(Web))!;
        Assert.Equal((AttendanceSources.AdHoc, AttendanceSessionStates.Open, Today, s.World.First.Id), (dto.Source, dto.State, dto.Date, dto.PeriodId));
        Assert.Equal((new TimeOnly(8, 0), new TimeOnly(8, 45)), (dto.StartsAt, dto.EndsAt));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(dto.Id, (await again.Content.ReadFromJsonAsync<AttendanceSessionListItemDto>(Web))!.Id);
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.attendance_session_future), (future.StatusCode, await CodeAsync(future)));

        var tooOld = await CreateAsync(s.Teacher, s.World.Math.Id, LocalToday.AddDays(-AttendanceLimits.SearchMaxDays - 3));
        var weekend = await CreateAsync(s.Teacher, s.World.Math.Id, FridayOnOrBefore(LocalToday));
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (tooOld.StatusCode, weekend.StatusCode)); // not a school day in range
    }

    private static DateOnly FridayOnOrBefore(DateOnly day)
    {
        while (day.DayOfWeek != DayOfWeek.Friday)
        {
            day = day.AddDays(-1);
        }

        return day;
    }

    [Fact]
    public async Task Two_identical_requests_at_once_create_one_session()
    {
        var s = await SetUpAsync("AH5");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateAsync(s.Teacher, s.World.Math.Id, Today, s.World.Second.Id)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, r.StatusCode.ToString()));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(1, await db.Set<AttendanceSession>().CountAsync(x => x.ClassCourseId == s.World.Math.Id && x.Date == Today && x.PeriodId == s.World.Second.Id));
    }

    [Fact]
    public async Task A_timetable_session_at_the_same_place_is_returned_instead_of_a_second_one()
    {
        var s = await SetUpAsync("AH6");
        Guid existing;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var slot = new TimetableSlot(s.World.Class, s.World.Math.Id, s.World.Term.Id, s.World.MathTeacher, Today.DayOfWeek, s.World.First.Id, null);
            slot.Publish();
            db.Add(slot);
            var session = new AttendanceSession(s.World.Math, Today, slot.Id, s.World.First.Id, AttendanceSession.TimetableSource);
            db.Add(session);
            await db.SaveChangesAsync();
            existing = session.Id;
        }

        var response = await CreateAsync(s.Teacher, s.World.Math.Id, Today, s.World.First.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(existing, (await response.Content.ReadFromJsonAsync<AttendanceSessionListItemDto>(Web))!.Id);
    }

    [Fact]
    public async Task Ad_hoc_session_needs_the_relationship_and_the_permission()
    {
        var s = await SetUpAsync("AH2");

        Assert.Equal(HttpStatusCode.NotFound, (await CreateAsync(s.OtherTeacher, s.World.Math.Id, Today)).StatusCode); // another teacher
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateAsync(s.Counselor, s.World.Math.Id, Today)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(s.Teacher, s.World.Math.Id, Today, Guid.NewGuid())).StatusCode); // an unknown period
    }

    [Fact]
    public async Task A_closed_class_takes_no_ad_hoc_session()
    {
        var s = await SetUpAsync("AH3");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Lms.Domain.Classes.Class>().SingleAsync(c => c.Id == s.World.Class.Id)).SetStatus(Lms.Domain.Classes.Class.ClosedStatus);
            await db.SaveChangesAsync();
        }

        var response = await CreateAsync(s.Teacher, s.World.Math.Id, Today);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Session_list_is_the_callers_own_and_includes_substitute_sessions()
    {
        var s = await SetUpAsync("AH4");
        var substituteUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var substitute = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Teacher, substituteUser);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(s.World.Science, substituteUser, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        var date = s.World.Term.StartDate;
        while (date.DayOfWeek != DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }

        TimetableSlot slot;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            slot = new TimetableSlot(s.World.Class, s.World.Math.Id, s.World.Term.Id, s.World.MathTeacher, DayOfWeek.Sunday, s.World.First.Id, null);
            slot.Publish();
            db.Add(slot);
            await db.SaveChangesAsync();
            db.Add(new TimetableException(slot, date, TimetableException.SubstituteKind, substituteUser, null, null));
            await db.SaveChangesAsync();
        }

        var generator = new AttendanceSessionGenerator(factory.Services.GetRequiredService<IConfiguration>(), new FixedClock(date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc)),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AttendanceSessionGenerator>.Instance);
        await generator.GenerateForOrganizationAsync(s.World.Unit.OrganizationId, null, null, default);

        async Task<List<AttendanceSessionListItemDto>> ListAsync(RoleAssignment who) =>
            (await (await SendAsync(factory, HttpMethod.Get, $"{Routes.AttendanceSessionsPattern}?date={date:yyyy-MM-dd}", who)).Content
                .ReadFromJsonAsync<List<AttendanceSessionListItemDto>>(Web))!;

        var owner = await ListAsync(s.Teacher);
        var asSubstitute = await ListAsync(substitute);
        var other = await ListAsync(s.OtherTeacher);

        Assert.True(asSubstitute.Single(x => x.ClassCourseId == s.World.Math.Id).IsSubstitute); // the substitute sees it, flagged by the server
        Assert.DoesNotContain(other, x => x.ClassCourseId == s.World.Math.Id); // another teacher does not
        // The teacher of record is substituted away for that date: the substitute is the teacher of record for that one session, so it leaves their list (L15).
        Assert.DoesNotContain(owner, x => x.ClassCourseId == s.World.Math.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionsPattern, s.Counselor)).StatusCode);
    }

    private async Task<AttendanceSession> SessionOfAsync(Guid id)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AttendanceSession>().SingleAsync(x => x.Id == id);
    }
}
