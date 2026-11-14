using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Configuration;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Enrollment;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>Summaries, reports, the unit session search, own and guardian reads and absence notices (M5 Task 17, FR-ATT-01, FR-EXC-01, L21).</summary>
[Collection(ApiCollection.Name)]
public sealed class AttendanceReadTests(LmsApiFactory factory)
{
    private sealed record Setup(
        AccessTree Tree, TimetableWorld World, RoleAssignment Teacher, RoleAssignment Counselor, RoleAssignment Guardian, RoleAssignment Student,
        Guid GuardianUser, Guid StudentUser, Guid SessionId, Guid[] Students);

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

    /// <summary>Three students; student 0 has a guardian and a login, student 1 has neither. Attendance: 0 Absent (with a private note), 1 Late, 2 Present.</summary>
    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var teacherUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var studentUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var students = new List<Guid>();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(world.Math, teacherUser, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 3; i++)
        {
            var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            var placement = new ClassStudent(world.Class, student.Id, new DateOnly(2020, 1, 1));
            db.Add(placement);
            db.Add(new CourseEnrollment(world.Math, placement, "Auto", new DateOnly(2020, 1, 1)));
            if (i == 0)
            {
                tracked.LinkUser(studentUser);
                var guardian = new GuardianProfile(tree.Org.Id, "ولي", "Guardian", PeopleTestKit.NewMobile(), null, null);
                guardian.LinkUser(guardianUser);
                db.Add(guardian);
                db.Add(StudentGuardian.For(tracked, guardian, "Father", true));
            }

            await db.SaveChangesAsync();
            students.Add(student.Id);
        }

        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacherUser);
        var session = (await (await SendAsync(factory, HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", world.Math.Id.ToString()), teacher,
            new CreateAdHocAttendanceSessionRequest(Today, world.First.Id))).Content.ReadFromJsonAsync<AttendanceSessionListItemDto>(Web))!;
        var taken = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(session.Id), teacher, new TakeAttendanceRequest(AttendanceStatuses.Present,
            [new TakeAttendanceRecord(students[0], AttendanceStatuses.Absent, "private-note-xyz"), new TakeAttendanceRecord(students[1], AttendanceStatuses.Late, null)], null, session.Version));
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        return new Setup(tree, world, teacher,
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, guardianUser),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, studentUser),
            guardianUser, studentUser, session.Id, [.. students]);
    }

    private async Task<OwnAttendanceDto> OwnAsync(RoleAssignment who, string url)
    {
        var response = await SendAsync(factory, HttpMethod.Get, url, who);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OwnAttendanceDto>(Web))!;
    }

    [Fact]
    public async Task Guardian_reads_only_own_child_and_can_submit_follows_the_submitters_setting()
    {
        var s = await SetUpAsync("RD1");

        var child = await OwnAsync(s.Guardian, Routes.StudentAttendance(s.Students[0]));
        Assert.Equal(new AttendanceSummaryDto(0, 1, 0, 0, 1, 0m), child.Summary);
        Assert.True(child.CanSubmitExcuse);
        var item = child.Items.Items.Single();
        Assert.Equal(AttendanceStatuses.Absent, item.Status);
        Assert.True(item.Excusable);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.StudentAttendance(s.Students[1]), s.Guardian)).StatusCode); // not their child
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.StudentAttendance(s.Students[0]), s.Student)).StatusCode); // no guardian link

        var mine = await OwnAsync(s.Student, Routes.MyAttendance());
        Assert.Equal(s.Students[0], mine.StudentId);
        Assert.False(mine.CanSubmitExcuse); // GuardiansOnly by default (D61)
        Assert.All(mine.Items.Items, i => Assert.False(i.Excusable));

        using (var scope = new EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(SettingKeys.ExcusesSubmitters, s.Tree.School1.Id, "GuardiansAndStudents", false);
        }

        var later = await OwnAsync(s.Student, Routes.MyAttendance());
        Assert.True(later.CanSubmitExcuse);
        Assert.True(later.Items.Items.Single().Excusable);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.MyAttendance(), s.Guardian)).StatusCode); // a guardian has no own attendance
    }

    [Fact]
    public async Task Absence_notice_goes_to_the_family_once_and_contains_no_note_text()
    {
        var s = await SetUpAsync("RD2");

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var guardianNotices = await db.Set<Notification>().Where(n => n.UserId == s.GuardianUser && n.EventKey == NotificationEvents.AttendanceAbsent).ToListAsync();
        var studentNotices = await db.Set<Notification>().Where(n => n.UserId == s.StudentUser && n.EventKey == NotificationEvents.AttendanceAbsent).ToListAsync();

        Assert.Single(guardianNotices); // student 1 (Late) has no guardian; student 2 (Present) is never reported
        Assert.Single(studentNotices);
        Assert.DoesNotContain("private-note-xyz", guardianNotices[0].ParametersJson);
        Assert.Contains("Absent", guardianNotices[0].ParametersJson);
    }

    [Fact]
    public async Task Summaries_count_statuses_over_a_range_and_the_unit_search_finds_taken_sessions()
    {
        var s = await SetUpAsync("RD3");

        var report = await SendAsync(factory, HttpMethod.Get, $"{Routes.AttendanceReport(AttendanceReportKinds.Class)}?classId={s.World.Class.Id}", s.Counselor);
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var dto = (await report.Content.ReadFromJsonAsync<AttendanceReportDto>(Web))!;
        Assert.Equal(new AttendanceSummaryDto(1, 1, 1, 0, 3, 66.67m), dto.Total); // Present + Late count as attending
        Assert.Equal(3, dto.Rows.Items.Count);
        var byStudent = (await (await SendAsync(factory, HttpMethod.Get, $"{Routes.AttendanceReport(AttendanceReportKinds.Student)}?studentId={s.Students[0]}", s.Counselor))
            .Content.ReadFromJsonAsync<AttendanceReportDto>(Web))!;
        Assert.Equal(1, byStudent.Total.Absent);

        var found = await SendAsync(factory, HttpMethod.Get, Routes.OrgUnitAttendanceSessions(s.Tree.School1.Id, Today.AddDays(-1), Today), s.Counselor);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var page = (await found.Content.ReadFromJsonAsync<PagedResult<AttendanceSessionListItemDto>>(Web))!;
        Assert.Equal(s.SessionId, page.Items.Single().Id);
        Assert.Equal(AttendanceSessionStates.Taken, page.Items[0].State);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(factory, HttpMethod.Get, Routes.OrgUnitAttendanceSessions(s.Tree.School1.Id, Today.AddDays(-200), Today), s.Counselor)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(factory, HttpMethod.Get, Routes.OrgUnitAttendanceSessions(s.Tree.School1.Id, Today.AddDays(-1), Today), s.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.OrgUnitAttendanceSessions(s.Tree.School2.Id, Today.AddDays(-1), Today), s.Counselor)).StatusCode);
    }
}
