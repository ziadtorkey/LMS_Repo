using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Excuses;
using Lms.Domain.Files;
using Lms.Domain.Notifications;
using Lms.Infrastructure;
using Lms.Infrastructure.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Excuses;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Excuses;

/// <summary>Submitting, withdrawing and approving excuses over real attendance sessions (M5 Task 19, FR-EXC-02/03, D61, L13, L21).</summary>
[Collection(ApiCollection.Name)]
public sealed class ExcuseSubmitTests(LmsApiFactory factory)
{
    private sealed record Setup(
        AccessTree Tree, TimetableWorld World, RoleAssignment Teacher, RoleAssignment Counselor, RoleAssignment Guardian, RoleAssignment Student,
        RoleAssignment OtherGuardian, Guid GuardianUser, Guid[] Students, Guid[] Sessions, Guid OldSession);

    /// <summary>The school day (Sunday to Thursday) that is <paramref name="schoolDays"/> school days before the latest one on or before the Riyadh date; 0 is that latest one.</summary>
    private static DateOnly SchoolDayBefore(int schoolDays)
    {
        static bool Weekend(DateOnly d) => d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday;
        var d = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)); // Asia/Riyadh (D64)
        while (Weekend(d))
        {
            d = d.AddDays(-1);
        }

        for (var left = schoolDays; left > 0; left--)
        {
            do
            {
                d = d.AddDays(-1);
            }
            while (Weekend(d));
        }

        return d;
    }

    /// <summary>Student 0 has a guardian and a login. Sessions: today-ish and the school day before (0 Absent in both, 1 Late in the first), and one 40 school days ago (0 Absent).</summary>
    private async Task<Setup> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var teacherUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var otherGuardianUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var studentUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var students = new List<Guid>();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(world.Math, teacherUser, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < 2; i++)
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
                var other = new GuardianProfile(tree.Org.Id, "ولي٢", "Guardian 2", PeopleTestKit.NewMobile(), null, null);
                other.LinkUser(otherGuardianUser);
                db.AddRange(guardian, other);
                db.Add(StudentGuardian.For(tracked, guardian, "Father", true));
            }

            await db.SaveChangesAsync();
            students.Add(student.Id);
        }

        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacherUser);
        async Task<Guid> TakeAsync(DateOnly date, Guid period, params (Guid Student, string Status)[] marks)
        {
            var session = (await (await SendAsync(factory, HttpMethod.Post, Routes.ClassCourseAttendanceSessionsPattern.Replace("{id:guid}", world.Math.Id.ToString()), teacher,
                new CreateAdHocAttendanceSessionRequest(date, period))).Content.ReadFromJsonAsync<AttendanceSessionListItemDto>(Web))!;
            var taken = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(session.Id), teacher, new TakeAttendanceRequest(AttendanceStatuses.Present,
                marks.Select(m => new TakeAttendanceRecord(m.Student, m.Status, null)).ToList(), null, session.Version));
            Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
            return session.Id;
        }

        var first = await TakeAsync(SchoolDayBefore(0), world.First.Id, (students[0], AttendanceStatuses.Absent), (students[1], AttendanceStatuses.Late));
        var second = await TakeAsync(SchoolDayBefore(1), world.First.Id, (students[0], AttendanceStatuses.Absent));
        var old = await TakeAsync(SchoolDayBefore(40), world.First.Id, (students[0], AttendanceStatuses.Absent));
        return new Setup(tree, world, teacher,
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, (await PeopleTestKit.AddUserAsync(factory)).Id),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, guardianUser),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, studentUser),
            await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, otherGuardianUser),
            guardianUser, [.. students], [first, second], old);
    }

    private Task<HttpResponseMessage> SubmitAsync(RoleAssignment who, Guid student, string reason, DateOnly? from, DateOnly? to, params Guid[] sessions) =>
        SendAsync(factory, HttpMethod.Post, Routes.ExcusesPattern, who,
            new SubmitExcuseRequest(student, reason, "private reason text", from, to, sessions.Length == 0 ? null : sessions, null));

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static async Task<string?> ViolationAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("extra").GetProperty("reason").GetString();

    private static async Task<(string? Code, string? Reason)> ProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("code").GetString(), json.TryGetProperty("extra", out var extra) ? extra.GetProperty("reason").GetString() : null);
    }

    private static async Task<ExcuseDto> DtoAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        return (await response.Content.ReadFromJsonAsync<ExcuseDto>(Web))!;
    }

    private async Task<Dictionary<Guid, string?>> StatusesAsync(Guid student, params Guid[] sessions)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AttendanceRecord>().Where(r => r.StudentProfileId == student && sessions.Contains(r.AttendanceSessionId))
            .ToDictionaryAsync(r => r.AttendanceSessionId, r => r.Status);
    }

    [Fact]
    public async Task Guardian_submits_by_dates_and_the_covered_sessions_are_the_real_absences_inside_the_deadline()
    {
        var s = await SetUpAsync("XS1");
        var range = (SchoolDayBefore(40), SchoolDayBefore(0));

        var response = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, range.Item1, range.Item2);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var excuse = await DtoAsync(response);
        Assert.Equal((ExcuseStatuses.Submitted, s.Students[0]), (excuse.Status, excuse.StudentId));
        Assert.Equal(s.Sessions.Order(), excuse.Sessions.Select(x => x.SessionId).Order()); // the 40-day-old absence is past the deadline and is left out
        Assert.All(excuse.Sessions, x => Assert.Equal(AttendanceStatuses.Absent, x.RecordStatus));
        Assert.True(excuse.CanWithdraw);
        Assert.Equal("private reason text", excuse.ReasonText);

        var again = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), range.Item2);
        Assert.Equal((HttpStatusCode.Conflict, (ProblemCodes.excuse_out_of_policy, ExcusePolicyViolations.NothingToExcuse)),
            (again.StatusCode, await ProblemAsync(again))); // already covered by the pending excuse
    }

    [Fact]
    public async Task Submit_by_session_ids_covers_only_the_chosen_absent_or_late_sessions()
    {
        var s = await SetUpAsync("XS2");

        var response = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Family, null, null, s.Sessions[1]);

        var excuse = await DtoAsync(response);
        Assert.Equal(s.Sessions[1], Assert.Single(excuse.Sessions).SessionId);
        var excusable = await SendAsync(factory, HttpMethod.Get, $"{Routes.StudentExcusableSessionsPattern.Replace("{id:guid}", s.Students[0].ToString())}?from={SchoolDayBefore(40):yyyy-MM-dd}&to={SchoolDayBefore(0):yyyy-MM-dd}", s.Guardian);
        var listed = (await excusable.Content.ReadFromJsonAsync<List<ExcusableSessionDto>>(Web))!;
        Assert.Equal(new[] { s.OldSession, s.Sessions[0] }.Order(), listed.Select(x => x.SessionId).Order()); // the covered one is gone; the old one is listed (the deadline applies at submit)
    }

    [Fact]
    public async Task Out_of_policy_submissions_are_refused_with_the_violation()
    {
        var s = await SetUpAsync("XS3");

        var tooLate = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, null, null, s.OldSession);
        var badReason = await SubmitAsync(s.Guardian, s.Students[0], "Holiday", SchoolDayBefore(1), SchoolDayBefore(0));
        var nothing = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(3), SchoolDayBefore(2));
        var future = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(0), SchoolDayBefore(0).AddDays(5));
        var both = await SendAsync(factory, HttpMethod.Post, Routes.ExcusesPattern, s.Guardian,
            new SubmitExcuseRequest(s.Students[0], ExcuseReasons.Illness, null, SchoolDayBefore(1), SchoolDayBefore(0), [s.Sessions[0]], null));

        Assert.Equal((ExcusePolicyViolations.TooLate, ExcusePolicyViolations.ReasonNotAllowed, ExcusePolicyViolations.NothingToExcuse, ExcusePolicyViolations.DatesInvalid),
            (await ViolationAsync(tooLate), await ViolationAsync(badReason), await ViolationAsync(nothing), await ViolationAsync(future)));
        Assert.All([tooLate, badReason, nothing, future], r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode); // exactly one of ids or dates
    }

    [Fact]
    public async Task Student_submit_follows_the_submitters_setting_and_strangers_get_404()
    {
        var s = await SetUpAsync("XS4");

        var asStudent = await SubmitAsync(s.Student, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0));
        var stranger = await SubmitAsync(s.OtherGuardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0));
        var unknown = await SubmitAsync(s.Guardian, Guid.NewGuid(), ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0));
        var staff = await SubmitAsync(s.Teacher, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0));

        Assert.Equal((HttpStatusCode.Forbidden, ProblemCodes.excuse_submitter_not_allowed), (asStudent.StatusCode, await CodeAsync(asStudent))); // GuardiansOnly by default (D61)
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (stranger.StatusCode, unknown.StatusCode));
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode); // staff hold no Excuses.Submit

        using (var scope = new Lms.IntegrationTests.Enrollment.EngineScope(factory.Services))
        {
            await scope.Settings.SetAsync(SettingKeys.ExcusesSubmitters, s.Tree.School1.Id, "GuardiansAndStudents", false);
        }

        var allowed = await SubmitAsync(s.Student, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0));
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        var policy = await SendAsync(factory, HttpMethod.Get, Routes.StudentExcusePolicy(s.Students[0]), s.Student);
        Assert.True((await policy.Content.ReadFromJsonAsync<ExcusePolicyDto>(Web))!.CanSubmit);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, Routes.StudentExcusePolicy(s.Students[0]), s.OtherGuardian)).StatusCode);
    }

    [Fact]
    public async Task Only_the_submitter_withdraws_and_only_while_submitted()
    {
        var s = await SetUpAsync("XS5");
        var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));

        var byOther = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseWithdraw(excuse.Id), s.OtherGuardian);
        var byCounselor = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseWithdraw(excuse.Id), s.Counselor);
        var withdrawn = await DtoAsync(await SendAsync(factory, HttpMethod.Post, Routes.ExcuseWithdraw(excuse.Id), s.Guardian));
        var twice = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseWithdraw(excuse.Id), s.Guardian);
        var approveLate = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseApprove(excuse.Id), s.Counselor, new ApproveExcuseRequest(null));

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.Forbidden), (byOther.StatusCode, byCounselor.StatusCode)); // another guardian reaches nothing; a counselor holds no Excuses.Submit
        Assert.Equal((ExcuseStatuses.Withdrawn, false), (withdrawn.Status, withdrawn.CanWithdraw));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.excuse_not_pending), (twice.StatusCode, await CodeAsync(twice)));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.excuse_not_pending), (approveLate.StatusCode, await CodeAsync(approveLate)));
        // A withdrawn excuse frees its sessions: they can be covered again.
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0))).StatusCode);
    }

    [Fact]
    public async Task Approve_flips_the_covered_records_to_excused_with_history_and_tells_the_submitter()
    {
        var s = await SetUpAsync("XS6");
        var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));
        // A teacher corrects one covered record to Present before the review: approval leaves it alone.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<AttendanceRecord>().SingleAsync(r => r.AttendanceSessionId == s.Sessions[1] && r.StudentProfileId == s.Students[0])).Set(AttendanceStatuses.Present, null);
            await db.SaveChangesAsync();
        }

        var response = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseApprove(excuse.Id), s.Counselor, new ApproveExcuseRequest("ok"));

        var approved = await DtoAsync(response);
        Assert.Equal((ExcuseStatuses.Approved, "ok"), (approved.Status, approved.DecisionComment));
        var statuses = await StatusesAsync(s.Students[0], s.Sessions);
        Assert.Equal((AttendanceStatuses.Excused, AttendanceStatuses.Present), (statuses[s.Sessions[0]], statuses[s.Sessions[1]]));
        Assert.Equal(AttendanceStatuses.Excused, approved.Sessions.Single(x => x.SessionId == s.Sessions[0]).RecordStatus);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var recordId = (await db.Set<AttendanceRecord>().SingleAsync(r => r.AttendanceSessionId == s.Sessions[0] && r.StudentProfileId == s.Students[0])).Id;
            var change = await db.Set<AttendanceRecordChange>().SingleAsync(c => c.AttendanceRecordId == recordId && c.Kind == AttendanceChangeKinds.ExcuseApproved);
            Assert.Equal((AttendanceStatuses.Absent, AttendanceStatuses.Excused), (change.FromStatus, change.ToStatus));
            Assert.Equal(1, await db.Set<Notification>().CountAsync(n => n.UserId == s.GuardianUser && n.EventKey == NotificationEvents.ExcusesDecided));
        }

        var again = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseApprove(excuse.Id), s.Counselor, new ApproveExcuseRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        // An approved excuse covers its sessions for good: no second excuse for them.
        var second = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Rejecting_leaves_the_records_absent_and_the_reviewer_is_never_the_submitter()
    {
        var s = await SetUpAsync("XS7");
        var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));
        var teacherApprove = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseApprove(excuse.Id), s.Teacher, new ApproveExcuseRequest(null));
        var guardianApprove = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseApprove(excuse.Id), s.Guardian, new ApproveExcuseRequest(null));

        var rejected = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseReject(excuse.Id), s.Counselor, new RejectExcuseRequest("no proof"));

        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (teacherApprove.StatusCode, guardianApprove.StatusCode));
        Assert.Equal(ExcuseStatuses.Rejected, (await DtoAsync(rejected)).Status);
        Assert.All((await StatusesAsync(s.Students[0], s.Sessions)).Values, v => Assert.Equal(AttendanceStatuses.Absent, v));
        // Rejected sessions are free again.
        Assert.Equal(HttpStatusCode.Created, (await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0))).StatusCode);
    }

    [Fact]
    public async Task My_excuses_lists_the_callers_own_and_a_childs_not_theirs_is_404()
    {
        var s = await SetUpAsync("XS8");
        var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));

        async Task<List<ExcuseDto>> ListAsync(RoleAssignment who, string query = "")
        {
            var response = await SendAsync(factory, HttpMethod.Get, Routes.MeExcuses + query, who);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PagedResult<ExcuseDto>>(Web))!.Items.ToList();
        }

        Assert.Equal(excuse.Id, Assert.Single(await ListAsync(s.Guardian)).Id);
        Assert.Equal(excuse.Id, Assert.Single(await ListAsync(s.Student)).Id); // a student also sees the ones for them
        Assert.Empty(await ListAsync(s.OtherGuardian));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Get, $"{Routes.MeExcuses}?studentId={s.Students[0]}", s.OtherGuardian)).StatusCode);
        Assert.Empty(await ListAsync(s.Guardian, "?status=Approved"));
    }

    /// <summary>Stores a file as the caller (the object really goes to MinIO and its CreatedBy is the caller).</summary>
    private async Task<Guid> StoreFileAsync(Setup s, Guid callerUser, string fileName, byte[] bytes)
    {
        var ctx = TestCurrentContext.For(s.Tree.Org.Id, s.Tree.School1.Path);
        ctx.UserId = callerUser;
        await using var db = factory.CreateDbContext(ctx);
        var template = factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value;
        var options = Options.Create(new StorageOptions
        {
            Endpoint = template.Endpoint, PublicEndpoint = template.PublicEndpoint, AccessKey = template.AccessKey, SecretKey = template.SecretKey,
            Region = template.Region, AllowedExtensions = template.AllowedExtensions,
        });
        var storage = new MinioFileStorage(db, new Lms.Infrastructure.Configuration.SettingsResolver(db, ctx), new MinioClients(options), options, new SystemClock());
        var stored = await storage.UploadAsync(new FileUploadRequest(fileName, "application/octet-stream", bytes.Length, s.Tree.School1.Id), new MemoryStream(bytes));
        return stored.Id;
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];

    [Fact]
    public async Task An_attachment_is_checked_by_its_content_at_submit_and_a_file_of_another_user_is_refused()
    {
        var s = await SetUpAsync("XS9");
        var fake = await StoreFileAsync(s, s.GuardianUser, "note.png", Encoding.ASCII.GetBytes("this is plain text, not a png at all"));
        var real = await StoreFileAsync(s, s.GuardianUser, "note.png", Png);
        var foreign = await StoreFileAsync(s, Guid.NewGuid(), "other.png", Png);
        Task<HttpResponseMessage> WithFile(Guid file) => SendAsync(factory, HttpMethod.Post, Routes.ExcusesPattern, s.Guardian,
            new SubmitExcuseRequest(s.Students[0], ExcuseReasons.Illness, null, SchoolDayBefore(1), SchoolDayBefore(0), null, file));

        var notAnImage = await WithFile(fake);
        var someoneElses = await WithFile(foreign);
        var missing = await WithFile(Guid.NewGuid());
        var ok = await WithFile(real);

        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.excuse_attachment_invalid), (notAnImage.StatusCode, await CodeAsync(notAnImage))); // D65: sniffed, not trusted by name
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (someoneElses.StatusCode, missing.StatusCode));
        var excuse = await DtoAsync(ok);
        Assert.True(excuse.HasAttachment);
        Assert.Equal("note.png", excuse.AttachmentFileName);
    }

    [Fact]
    public async Task An_absence_older_than_the_calendar_can_count_is_too_late_not_a_server_error()
    {
        var s = await SetUpAsync("XS10");
        var ancient = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-420);
        while (ancient.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
        {
            ancient = ancient.AddDays(-1);
        }

        // Ad hoc sessions reach back at most 92 days, so the old session is written directly, as a Taken session with the student Absent.
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.World.Math.Id);
            var old = new AttendanceSession(classCourse, ancient, null, s.World.Second.Id, AttendanceSession.AdHocSource);
            old.RecordSave(Guid.NewGuid(), DateTime.UtcNow);
            db.Add(old);
            db.Add(new AttendanceRecord(old, s.Students[0], AttendanceStatuses.Absent, null));
            await db.SaveChangesAsync();
        }

        var byDates = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, ancient, ancient);
        var wide = await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, ancient.AddDays(-5), SchoolDayBefore(0));

        Assert.Equal((HttpStatusCode.Conflict, (ProblemCodes.excuse_out_of_policy, ExcusePolicyViolations.TooLate)), (byDates.StatusCode, await ProblemAsync(byDates)));
        Assert.Equal(HttpStatusCode.Created, wide.StatusCode); // the recent absences in a wide range are still coverable; the ancient one is left out
        Assert.DoesNotContain((await DtoAsync(wide)).Sessions, x => x.Date == ancient);
    }

    [Fact]
    public async Task An_unlinked_former_guardian_reads_and_withdraws_nothing_any_more()
    {
        var s = await SetUpAsync("XS11");
        var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var link = await db.Set<StudentGuardian>().SingleAsync(l => l.StudentId == s.Students[0]);
            db.Remove(link); // the guardian is unlinked from the child
            await db.SaveChangesAsync();
        }

        var list = await SendAsync(factory, HttpMethod.Get, Routes.MeExcuses, s.Guardian);
        var withdraw = await SendAsync(factory, HttpMethod.Post, Routes.ExcuseWithdraw(excuse.Id), s.Guardian);
        var policy = await SendAsync(factory, HttpMethod.Get, Routes.StudentExcusePolicy(s.Students[0]), s.Guardian);

        Assert.Empty((await list.Content.ReadFromJsonAsync<PagedResult<ExcuseDto>>(Web))!.Items);
        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound), (withdraw.StatusCode, policy.StatusCode));
        await using var read = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(ExcuseStatuses.Submitted, (await read.Set<AbsenceExcuse>().SingleAsync(e => e.Id == excuse.Id)).Status); // untouched
    }

    [Fact]
    public async Task A_second_decision_on_a_stale_excuse_loses_with_a_concurrency_check()
    {
        var s = await SetUpAsync("XS12");
        var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));
        await using var first = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        await using var second = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var loadedFirst = await first.Set<AbsenceExcuse>().SingleAsync(e => e.Id == excuse.Id);
        var loadedSecond = await second.Set<AbsenceExcuse>().SingleAsync(e => e.Id == excuse.Id);
        loadedFirst.Withdraw(loadedFirst.SubmittedByUserId, DateTime.UtcNow);
        await first.SaveChangesAsync();

        loadedSecond.Approve(Guid.NewGuid(), null, DateTime.UtcNow); // a reviewer who loaded it before the withdrawal

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Approve_racing_withdraw_has_one_winner_and_the_records_match_it()
    {
        for (var round = 0; round < 3; round++)
        {
            var s = await SetUpAsync("XR" + round);
            var excuse = await DtoAsync(await SubmitAsync(s.Guardian, s.Students[0], ExcuseReasons.Illness, SchoolDayBefore(1), SchoolDayBefore(0)));

            var approve = SendAsync(factory, HttpMethod.Post, Routes.ExcuseApprove(excuse.Id), s.Counselor, new ApproveExcuseRequest(null));
            var withdraw = SendAsync(factory, HttpMethod.Post, Routes.ExcuseWithdraw(excuse.Id), s.Guardian);
            var results = await Task.WhenAll(approve, withdraw);

            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var status = (await db.Set<AbsenceExcuse>().SingleAsync(e => e.Id == excuse.Id)).Status;
            var statuses = (await StatusesAsync(s.Students[0], s.Sessions)).Values.ToList();
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); // exactly one decision won
            Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
            if (status == ExcuseStatuses.Approved)
            {
                Assert.All(statuses, v => Assert.Equal(AttendanceStatuses.Excused, v));
            }
            else
            {
                Assert.Equal(ExcuseStatuses.Withdrawn, status);
                Assert.All(statuses, v => Assert.Equal(AttendanceStatuses.Absent, v)); // never Withdrawn with Excused records
            }
        }
    }
}
