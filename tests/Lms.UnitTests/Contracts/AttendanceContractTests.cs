using System.Text.Json;
using System.Text.RegularExpressions;
using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Auditing;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;

namespace Lms.UnitTests.Contracts;

/// <summary>M5 contract, wave 3 (plan Task 3): attendance sessions and records, the live proposal, summaries and absence alerts.</summary>
public sealed class AttendanceContractTests
{
    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;
    private static readonly Guid Id = Guid.Parse("0198a6f2-0000-7000-8000-00000000002a");
    private static readonly Guid Other = Guid.Parse("0198a6f2-0000-7000-8000-00000000002b");

    private static readonly string[] AttendancePermissions = ["Attendance.Take", "Attendance.Edit", "Attendance.View", "Attendance.ViewOwn"];

    private static IReadOnlyList<string> AttendanceOf(string role) =>
        BuiltInRoles.Get(role).DefaultPermissions.Where(p => p.StartsWith("Attendance.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public void Attendance_permissions_follow_the_matrix()
    {
        string[] declared = [Permissions.Attendance.Take, Permissions.Attendance.Edit, Permissions.Attendance.View, Permissions.Attendance.ViewOwn];
        Assert.Equal(AttendancePermissions, declared);
        Assert.All(AttendancePermissions, p => Assert.Contains(p, Permissions.All));

        string[] staffAdmin = ["Attendance.Edit", "Attendance.Take", "Attendance.View"];
        Assert.Equal(staffAdmin, AttendanceOf(BuiltInRoles.OrgAdmin));
        Assert.Equal(staffAdmin, AttendanceOf(BuiltInRoles.Manager));
        Assert.Equal(["Attendance.Take"], AttendanceOf(BuiltInRoles.Teacher));
        Assert.Equal(["Attendance.View"], AttendanceOf(BuiltInRoles.Supervisor));
        Assert.Equal(["Attendance.Edit", "Attendance.View"], AttendanceOf(BuiltInRoles.Counselor));
        Assert.Equal(["Attendance.ViewOwn"], AttendanceOf(BuiltInRoles.Student));
        Assert.Equal(["Attendance.ViewOwn"], AttendanceOf(BuiltInRoles.Guardian));
        Assert.DoesNotContain(Permissions.Attendance.Edit, AttendanceOf(BuiltInRoles.Teacher)); // only a manager or counselor edits after the window (L14)
        Assert.DoesNotContain(Permissions.Attendance.Take, AttendanceOf(BuiltInRoles.Counselor));
    }

    [Fact]
    public void Counselor_has_no_grade_permissions()
    {
        // L16: the counselor reads attendance and reviews excuses; grades are not theirs.
        Assert.DoesNotContain(BuiltInRoles.Get(BuiltInRoles.Counselor).DefaultPermissions, p => p.StartsWith("Grades.", StringComparison.Ordinal));
    }

    [Fact]
    public void Attendance_relationships_use_own_class_courses_and_supervised_courses()
    {
        Assert.Equal(Relationships.OwnClassCourses, RelationshipRules.For(BuiltInRoles.Teacher, Permissions.Attendance.Take));
        Assert.Equal(Relationships.SupervisedCourses, RelationshipRules.For(BuiltInRoles.Supervisor, Permissions.Attendance.View));
        Assert.Null(RelationshipRules.For(BuiltInRoles.Counselor, Permissions.Attendance.View)); // unit subtree, not a class-course relationship
        Assert.Null(RelationshipRules.For(BuiltInRoles.Manager, Permissions.Attendance.Edit)); // unit scope
        Assert.Null(RelationshipRules.For(BuiltInRoles.Student, Permissions.Attendance.ViewOwn)); // self
    }

    [Fact]
    public void Own_attendance_is_self_service_and_staff_permissions_are_not()
    {
        Assert.Contains(Permissions.Attendance.ViewOwn, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Attendance.Take, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Attendance.Edit, Permissions.SelfService);
        Assert.DoesNotContain(Permissions.Attendance.View, Permissions.SelfService);
    }

    [Fact]
    public void Attendance_permissions_came_in_defaults_version_10_and_the_current_version_is_11()
    {
        Assert.Equal(11, BuiltInRoleDefaults.CurrentVersion);
        Assert.All(AttendancePermissions, p => Assert.Equal(10, BuiltInRoleDefaults.IntroducedIn(p)));
        Assert.Contains("Excuses.Submit", BuiltInRoleDefaults.IntroducedInVersion10);
        Assert.Equal(7, BuiltInRoleDefaults.IntroducedInVersion10.Count);
        Assert.All(BuiltInRoleDefaults.IntroducedInVersion10, p => Assert.Contains(p, Permissions.All));
    }

    [Fact]
    public void Attendance_catalogs_match_the_plan()
    {
        Assert.Equal(["Present", "Absent", "Late", "Excused"], AttendanceStatuses.All);
        Assert.Equal(["Timetable", "AdHoc", "Live"], AttendanceSources.All);
        Assert.Equal(["Open", "Taken"], AttendanceSessionStates.All);
        Assert.Equal(["Taken", "Edited", "EditedAfterWindow", "ExcuseApproved"], AttendanceChangeKinds.All);
        Assert.Equal(["student", "class"], AttendanceReportKinds.All);

        foreach (var list in new[] { AttendanceStatuses.All, AttendanceSources.All, AttendanceSessionStates.All, AttendanceChangeKinds.All, AttendanceReportKinds.All })
        {
            Assert.Equal(list.Count, list.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void Attendance_limits_and_settings_match_the_plan_and_d61()
    {
        Assert.Equal((48, 7, 50, 10), (AttendanceLimits.EditWindowHoursDefault, AttendanceLimits.GenerateDaysAheadDefault, AttendanceLimits.LiveMinPercentDefault, AttendanceLimits.LateAfterMinutesDefault));
        Assert.Equal((5, 30, 50), (AttendanceLimits.AbsentThresholdDefault, AttendanceLimits.ThresholdWindowDaysDefault, AttendanceLimits.LateWeightPercentDefault)); // D61
        Assert.Equal((1, 500, 200), (AttendanceLimits.ReasonMinLength, AttendanceLimits.ReasonMaxLength, AttendanceLimits.RosterMaxRows));

        Assert.Equal(7, SettingKeys.AttendanceGenerateDaysAhead.Default);
        Assert.Equal(48, SettingKeys.AttendanceEditWindowHours.Default);
        Assert.Equal(50, SettingKeys.AttendanceLiveMinPercent.Default);
        Assert.Equal(10, SettingKeys.AttendanceLateAfterMinutes.Default);
        Assert.Equal(5, SettingKeys.AttendanceAbsentThreshold.Default);
        Assert.Equal(30, SettingKeys.AttendanceThresholdWindowDays.Default);
        Assert.Equal(50, SettingKeys.AttendanceLateWeightPercent.Default);

        foreach (var (key, min, max) in new[]
                 {
                     (SettingKeys.AttendanceGenerateDaysAhead.Name, 1, 30), (SettingKeys.AttendanceEditWindowHours.Name, 1, 720),
                     (SettingKeys.AttendanceAbsentThreshold.Name, 1, 100), (SettingKeys.AttendanceThresholdWindowDays.Name, 7, 365),
                     (SettingKeys.AttendanceLateWeightPercent.Name, 0, 100),
                 })
        {
            var definition = SettingCatalog.Find(key)!;
            Assert.Equal((min, max), (definition.Min, definition.Max));
            Assert.False(definition.ClientLevelOnly); // a unit may choose its own attendance rules
        }
    }

    [Fact]
    public void Attendance_problem_codes_exist()
    {
        Assert.Equal(
            ["attendance_edit_window_closed", "attendance_session_future", "attendance_unmarked_rows", "attendance_reason_required", "attendance_not_a_proposal"],
            new[]
            {
                ProblemCodes.attendance_edit_window_closed, ProblemCodes.attendance_session_future, ProblemCodes.attendance_unmarked_rows,
                ProblemCodes.attendance_reason_required, ProblemCodes.attendance_not_a_proposal,
            });
    }

    [Fact]
    public void Attendance_entities_are_audited()
    {
        Assert.Contains("AttendanceRecordChange", AuditCatalog.EntityTypes);
    }

    [Fact]
    public void Attendance_notifications_name_the_student_and_never_a_grade()
    {
        var absent = NotificationEvents.Find(NotificationEvents.AttendanceAbsent)!;
        var threshold = NotificationEvents.Find(NotificationEvents.AttendanceThresholdCrossed)!;

        Assert.Equal("Attendance.Absent", absent.Key);
        Assert.Empty(absent.DefaultRecipientRoles); // named recipients: the student's guardians, only on Taken or a confirmed proposal (L11)
        Assert.Equal(["StudentNameAr", "StudentNameEn", "CourseNameAr", "CourseNameEn", "Date", "Status"], absent.Parameters);
        Assert.Equal("Attendance.ThresholdCrossed", threshold.Key);
        Assert.Equal([BuiltInRoles.Counselor], threshold.DefaultRecipientRoles);
        Assert.Equal(["StudentNameAr", "StudentNameEn", "Absences", "WindowDays"], threshold.Parameters);
    }

    [Fact]
    public void Attendance_routes_match_the_plan()
    {
        Assert.Equal("/api/attendance/sessions", Routes.AttendanceSessionsPattern);
        Assert.Equal("/api/class-courses/{id:guid}/attendance-sessions", Routes.ClassCourseAttendanceSessionsPattern);
        Assert.Equal("/api/attendance-sessions/{id:guid}/records", Routes.AttendanceSessionRecordsPattern);
        Assert.Equal("/api/attendance-sessions/{id:guid}/history", Routes.AttendanceSessionHistoryPattern);
        Assert.Equal("/api/attendance-sessions/{id:guid}/confirm", Routes.AttendanceSessionConfirmPattern);
        Assert.Equal("/api/attendance-sessions/{id:guid}/live-mappings", Routes.AttendanceSessionLiveMappingsPattern);
        Assert.Equal("/api/attendance-reports/{kind}", Routes.AttendanceReportPattern);
        Assert.Equal("/api/attendance-reports/{kind}/export", Routes.AttendanceReportExportPattern);
        Assert.Equal("/api/me/attendance", Routes.MeAttendance);
        Assert.Equal("/api/students/{id:guid}/attendance", Routes.StudentAttendancePattern);
        Assert.Equal("/api/attendance/alerts", Routes.AttendanceAlertsPattern);
        Assert.Equal("/api/org-units/{id:guid}/attendance-sessions", Routes.OrgUnitAttendanceSessionsPattern);

        Assert.Equal("/api/attendance/sessions", Routes.AttendanceSessions());
        Assert.Equal("/api/attendance/sessions?date=2027-01-05", Routes.AttendanceSessions(new DateOnly(2027, 1, 5)));
        Assert.Equal($"/api/class-courses/{Id}/attendance-sessions", Routes.ClassCourseAttendanceSessions(Id));
        Assert.Equal($"/api/org-units/{Id}/attendance-sessions?from=2027-01-01&to=2027-01-31", Routes.OrgUnitAttendanceSessions(Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)));
        Assert.Equal($"/api/org-units/{Id}/attendance-sessions?from=2027-01-01&to=2027-01-31&classId={Other}", Routes.OrgUnitAttendanceSessions(Id, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), classId: Other));
        Assert.Equal($"/api/attendance-sessions/{Id}/records", Routes.AttendanceSessionRecords(Id));
        Assert.Equal($"/api/attendance-sessions/{Id}/live-mappings", Routes.AttendanceSessionLiveMappings(Id));
        Assert.Equal("/api/attendance-reports/class/export", Routes.AttendanceReportExport(AttendanceReportKinds.Class));
        Assert.Equal("/api/me/attendance", Routes.MyAttendance());
        Assert.Equal("/api/me/attendance?from=2027-01-01&to=2027-01-31", Routes.MyAttendance(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)));
        Assert.Equal($"/api/students/{Other}/attendance?to=2027-01-31", Routes.StudentAttendance(Other, to: new DateOnly(2027, 1, 31)));

        StudentContractTests.AssertUniqueApiRoutes();
    }

    [Fact]
    public void Every_attendance_route_has_a_permission_doc_comment()
    {
        // The doc comment of each route constant is its row of the plan's route table; Task 40 tests every row against it.
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Lms.Shared", "Routes.Attendance.cs"));
        var matches = Regex.Matches(text, @"((?:\s*///[^\n]*\n)+)\s*public const string (\w+Pattern|MeAttendance) =");
        var routes = matches.Select(m => (Name: m.Groups[2].Value, Doc: m.Groups[1].Value)).ToList();

        Assert.True(routes.Count >= 12, $"found {routes.Count} documented route constants");
        Assert.All(routes, r => Assert.Matches(@"Attendance\.\w+|Excuses\.\w+", r.Doc));
        Assert.Equal(Regex.Matches(text, @"public const string \w+Pattern =").Count + 1, routes.Count); // + MeAttendance
    }

    [Fact]
    public void Attendance_dtos_round_trip()
    {
        var now = DateTime.UtcNow;
        var session = new AttendanceSessionListItemDto(Id, Other, "رياضيات", "Math", "١/أ", "1/A", new DateOnly(2027, 1, 5), Other,
            new TimeOnly(8, 0), new TimeOnly(8, 45), AttendanceSources.Live, AttendanceSessionStates.Open, 28, 30, null, true, true, 3, now);
        var record = new AttendanceRecordDto(Id, "سارة", "Sara", "000123", AttendanceStatuses.Late, "bus", 40, now, true);
        var unmatched = new UnmatchedParticipantDto(Other, "Guest 1", 30, now, null);
        var records = new AttendanceRecordsDto(session, [record], [unmatched], false);
        var take = new TakeAttendanceRequest(AttendanceStatuses.Present, [new TakeAttendanceRecord(Id, AttendanceStatuses.Absent, "sick")], "typo", 3);
        var confirm = new ConfirmAttendanceRequest(4);
        var mapping = new LiveMappingRequest([new LiveMappingEntry(Other, Id), new LiveMappingEntry(Id, null)], 4);
        var change = new AttendanceChangeDto(Id, Other, "سارة", "Sara", AttendanceChangeKinds.EditedAfterWindow, AttendanceStatuses.Absent, AttendanceStatuses.Present,
            "typo", Other, "المعلم", "Teacher", now);
        var summary = new AttendanceSummaryDto(20, 3, 2, 1, 26, 92.31m);
        var own = new OwnAttendanceDto(Id, "سارة", "Sara", new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), summary,
            new PagedResult<OwnAttendanceItemDto>([new OwnAttendanceItemDto(Id, new DateOnly(2027, 1, 5), "رياضيات", "Math", null, null, AttendanceStatuses.Absent, null, true, null, null)], 1, 1, 50),
            true, now);
        var report = new AttendanceReportDto(AttendanceReportKinds.Class, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), summary,
            new PagedResult<AttendanceReportRowDto>([new AttendanceReportRowDto(Id, "سارة", "Sara", "000123", null, null, null, summary)], 1, 1, 50), now, now);
        var alert = new AttendanceAlertDto(Id, Other, "سارة", "Sara", "000123", Other, 4, 2, 30, 5m, 5m, now, null);
        var adHoc = new CreateAdHocAttendanceSessionRequest(new DateOnly(2027, 1, 5), null);

        Assert.Equal(AttendanceSources.Live, RoundTrip(session).Source);
        Assert.Equal(new TimeOnly(8, 45), RoundTrip(session).EndsAt);
        Assert.True(RoundTrip(session).IsSubstitute);
        Assert.True(RoundTrip(record).Proposed);
        Assert.Equal(40, RoundTrip(record).JoinedMinutes);
        Assert.Equal("Guest 1", RoundTrip(records).Unmatched.Single().DisplayName);
        Assert.False(RoundTrip(records).ParticipantsAvailable);
        Assert.Equal(AttendanceStatuses.Present, RoundTrip(take).DefaultStatus);
        Assert.Equal("typo", RoundTrip(take).Reason);
        Assert.Equal(4, RoundTrip(confirm).Version);
        Assert.Null(RoundTrip(mapping).Mappings[1].StudentId);
        Assert.Equal(AttendanceChangeKinds.EditedAfterWindow, RoundTrip(change).Kind);
        Assert.Equal(92.31m, RoundTrip(summary).AttendancePercent);
        Assert.True(RoundTrip(own).CanSubmitExcuse);
        Assert.True(RoundTrip(own).Items.Items.Single().Excusable);
        Assert.Equal(AttendanceReportKinds.Class, RoundTrip(report).Kind);
        Assert.Equal(5m, RoundTrip(alert).Score);
        Assert.Equal(new DateOnly(2027, 1, 5), RoundTrip(adHoc).Date);
        Assert.Equal((1, Paging.DefaultPageSize), (new OwnAttendanceQuery().Page, new OwnAttendanceQuery().PageSize));
        Assert.Equal((1, Paging.DefaultPageSize), (new AttendanceReportQuery().Page, new AttendanceReportQuery().PageSize));
        Assert.True(new AttendanceAlertsQuery().ActiveOnly);
        Assert.Null(new AttendanceSessionsQuery().Date);
        var search = new AttendanceSessionSearchQuery(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), Id, null, AttendanceSessionStates.Taken);
        Assert.Equal(AttendanceSessionStates.Taken, RoundTrip(search).State);
        Assert.Equal((1, Paging.DefaultPageSize, 92), (search.Page, search.PageSize, AttendanceLimits.SearchMaxDays));
    }

    [Fact]
    public void A_live_session_list_item_links_to_its_attendance_session_when_it_exists()
    {
        var start = DateTime.UtcNow;
        var without = new Lms.Shared.LiveSessions.LiveSessionListItemDto(Id, Other, null, null, "حصة", "Class", start, start.AddMinutes(45), "Ended", false, start);
        var with = without with { AttendanceSessionId = Other };

        Assert.Null(RoundTrip(without).AttendanceSessionId); // null until the attendance session exists
        Assert.Equal(Other, RoundTrip(with).AttendanceSessionId);
    }

    [Fact]
    public void Every_timed_attendance_dto_carries_the_server_clock()
    {
        Type[] timed = [typeof(AttendanceSessionListItemDto), typeof(OwnAttendanceDto), typeof(AttendanceReportDto)];

        Assert.All(timed, t => Assert.Contains(t.GetProperties(), p => p.Name == "ServerNowUtc" && p.PropertyType == typeof(DateTime)));
        Assert.Contains(typeof(AttendanceReportDto).GetProperties(), p => p.Name == "ComputedAtUtc" && p.PropertyType == typeof(DateTime));
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LMS.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root (LMS.slnx) not found above " + AppContext.BaseDirectory);
    }
}
