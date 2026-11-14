using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>Taking attendance, the edit window, history and relationship checks (M5 Task 17, FR-ATT-01, L12, L14, L15).</summary>
[Collection(ApiCollection.Name)]
public sealed class TakeAttendanceTests(LmsApiFactory factory)
{
    private sealed record Setup(
        AccessTree Tree, TimetableWorld World, RoleAssignment Teacher, RoleAssignment OtherTeacher, RoleAssignment Counselor, RoleAssignment Student,
        Guid SessionId, Guid[] Students);

    /// <summary>The latest school day (Sunday to Thursday) on or before the Riyadh date: ad hoc sessions are for school days.</summary>
    private static DateOnly Today
    {
        get
        {
            var d = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // Asia/Riyadh (D64)
            while (d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
            {
                d = d.AddDays(-1);
            }

            return d;
        }
    }

    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var mathTeacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var otherTeacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var students = new List<Guid>();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(world.Math, mathTeacher, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            db.Add(new ClassCourseTeacher(world.Science, otherTeacher, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 3; i++)
        {
            var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var placement = new ClassStudent(world.Class, student.Id, new DateOnly(2020, 1, 1));
            db.Add(placement);
            db.Add(new CourseEnrollment(world.Math, placement, "Auto", new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
            students.Add(student.Id);
        }

        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, mathTeacher);
        var session = await (await SendAsync(factory, HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", world.Math.Id.ToString()), teacher,
            new CreateAdHocAttendanceSessionRequest(Today, world.First.Id))).Content.ReadFromJsonAsync<AttendanceSessionListItemDto>(Web);
        return new Setup(tree, world, teacher,
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, otherTeacher),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, (await PeopleTestKit.AddUserAsync(factory)).Id),
            session!.Id, [.. students]);
    }

    private Task<HttpResponseMessage> PutAsync(RoleAssignment who, Guid sessionId, TakeAttendanceRequest body) =>
        SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(sessionId), who, body);

    private async Task<AttendanceRecordsDto> GetAsync(RoleAssignment who, Guid sessionId)
    {
        var response = await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionRecords(sessionId), who);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttendanceRecordsDto>(Web))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private async Task SetTakenAtAsync(Guid sessionId, DateTime takenAtUtc)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var session = await db.Set<AttendanceSession>().SingleAsync(s => s.Id == sessionId);
        db.Entry(session).Property(nameof(AttendanceSession.TakenAtUtc)).CurrentValue = takenAtUtc;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Mark_all_present_with_two_exceptions_in_one_call_and_every_change_is_in_the_history()
    {
        var s = await SetUpAsync("TK1");
        var open = await GetAsync(s.Teacher, s.SessionId);
        Assert.Equal(3, open.Records.Count);
        Assert.All(open.Records, r => Assert.Null(r.Status)); // unmarked until taken

        var response = await PutAsync(s.Teacher, s.SessionId, new TakeAttendanceRequest(AttendanceStatuses.Present,
            [new TakeAttendanceRecord(s.Students[1], AttendanceStatuses.Absent, "sick"), new TakeAttendanceRecord(s.Students[2], AttendanceStatuses.Late, null)], null, open.Session.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var taken = (await response.Content.ReadFromJsonAsync<AttendanceRecordsDto>(Web))!;
        Assert.Equal(AttendanceSessionStates.Taken, taken.Session.State);
        Assert.Equal(open.Session.Version + 1, taken.Session.Version);
        Assert.NotNull(taken.Session.EditableUntilUtc);
        Assert.Equal(3, taken.Session.MarkedCount);
        Assert.Equal([AttendanceStatuses.Present, AttendanceStatuses.Absent, AttendanceStatuses.Late],
            s.Students.Select(id => taken.Records.Single(r => r.StudentId == id).Status));
        Assert.Equal("sick", taken.Records.Single(r => r.StudentId == s.Students[1]).Note);

        var history = (await (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionHistory(s.SessionId), s.Teacher)).Content.ReadFromJsonAsync<List<AttendanceChangeDto>>(Web))!;
        Assert.Equal(3, history.Count);
        Assert.All(history, h => Assert.Equal(AttendanceChangeKinds.Taken, h.Kind));
    }

    [Fact]
    public async Task A_taken_session_cannot_have_unmarked_rows_and_a_stale_version_is_409()
    {
        var s = await SetUpAsync("TK2");
        var open = await GetAsync(s.Teacher, s.SessionId);

        var unmarked = await PutAsync(s.Teacher, s.SessionId, new TakeAttendanceRequest(null, [new TakeAttendanceRecord(s.Students[0], AttendanceStatuses.Present, null)], null, open.Session.Version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unmarked.StatusCode);
        Assert.Equal(ProblemCodes.attendance_unmarked_rows, await CodeAsync(unmarked));

        var ok = await PutAsync(s.Teacher, s.SessionId, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, open.Session.Version));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var stale = await PutAsync(s.Teacher, s.SessionId, new TakeAttendanceRequest(AttendanceStatuses.Absent, [], null, open.Session.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(ProblemCodes.concurrency_conflict, await CodeAsync(stale));
    }

    [Fact]
    public async Task Teacher_edit_after_the_window_is_409_and_a_counselor_edits_with_a_reason()
    {
        var s = await SetUpAsync("TK3");
        var open = await GetAsync(s.Teacher, s.SessionId);
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(s.Teacher, s.SessionId, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, open.Session.Version))).StatusCode);
        await SetTakenAtAsync(s.SessionId, DateTime.UtcNow.AddDays(-3)); // the 48 hour window is over
        var version = (await GetAsync(s.Teacher, s.SessionId)).Session.Version;
        var change = new[] { new TakeAttendanceRecord(s.Students[0], AttendanceStatuses.Absent, null) };

        var teacherEdit = await PutAsync(s.Teacher, s.SessionId, new TakeAttendanceRequest(null, change, "typo", version));
        Assert.Equal(HttpStatusCode.Conflict, teacherEdit.StatusCode);
        Assert.Equal(ProblemCodes.attendance_edit_window_closed, await CodeAsync(teacherEdit));

        var noReason = await PutAsync(s.Counselor, s.SessionId, new TakeAttendanceRequest(null, change, null, version));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noReason.StatusCode);
        Assert.Equal(ProblemCodes.attendance_reason_required, await CodeAsync(noReason));

        var counselorEdit = await PutAsync(s.Counselor, s.SessionId, new TakeAttendanceRequest(null, change, "wrong student marked", version));
        Assert.Equal(HttpStatusCode.OK, counselorEdit.StatusCode);
        var history = (await (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionHistory(s.SessionId), s.Counselor)).Content.ReadFromJsonAsync<List<AttendanceChangeDto>>(Web))!;
        var edit = history.Single(h => h.Kind == AttendanceChangeKinds.EditedAfterWindow);
        Assert.Equal((AttendanceStatuses.Present, AttendanceStatuses.Absent, "wrong student marked"), (edit.FromStatus, edit.ToStatus, edit.Reason));
    }

    [Fact]
    public async Task Another_teacher_gets_404_and_a_student_is_refused()
    {
        var s = await SetUpAsync("TK4");

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionRecords(s.SessionId), s.OtherTeacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(s.OtherTeacher, s.SessionId, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionHistory(s.SessionId), s.OtherTeacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionRecords(s.SessionId), s.Student)).StatusCode);
        // The counselor can read (Attendance.View) but cannot take an Open session (no Attendance.Take).
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionRecords(s.SessionId), s.Counselor)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(s.Counselor, s.SessionId, new TakeAttendanceRequest(AttendanceStatuses.Present, [], null, 1))).StatusCode);
    }
}
