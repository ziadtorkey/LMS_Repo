using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Attendance;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.LiveSessions;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Attendance.Live;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Imports;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.IntegrationTests.Timetable;
using Lms.Shared;
using Lms.Shared.Attendance;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;
using static Lms.IntegrationTests.Timetable.TimetableKit;

namespace Lms.IntegrationTests.Attendance;

/// <summary>The live-session attendance proposal: the job, the sweep, the mapping and the confirmation (M5 Task 18, FR-TT-06, L11).</summary>
[Collection(ApiCollection.Name)]
public sealed class LiveProposalTests(LmsApiFactory factory)
{
    private sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed record Setup(
        AccessTree Tree, TimetableWorld World, RoleAssignment Teacher, Guid[] Students, LiveSession Live, Guid GuardianUser, Guid ClassCourseId);

    /// <summary>The latest school day (Sunday to Thursday) on or before the Riyadh date.</summary>
    private static DateOnly SchoolDay
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

    private LiveAttendanceProposer Proposer() =>
        new(factory.Services.GetRequiredService<IConfiguration>(), new SystemClock(), NullLogger<LiveAttendanceProposer>.Instance);

    private async Task<Setup> SetUpAsync(string name, bool imported = true, int studentCount = 3)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var world = await NewWorldAsync(factory, tree.School1);
        var teacherUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var students = new List<Guid>();
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(world.Math, teacherUser, ClassCourseTeacher.AssistantRole, new DateOnly(2020, 1, 1)));
            await db.SaveChangesAsync();
        }

        for (var i = 0; i < studentCount; i++)
        {
            var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var placement = new ClassStudent(world.Class, student.Id, new DateOnly(2020, 1, 1));
            db.Add(placement);
            db.Add(new CourseEnrollment(world.Math, placement, "Auto", new DateOnly(2020, 1, 1)));
            if (i == 0)
            {
                var guardian = new GuardianProfile(tree.Org.Id, "ولي", "Guardian", PeopleTestKit.NewMobile(), null, null);
                guardian.LinkUser(guardianUser);
                db.Add(guardian);
                db.Add(StudentGuardian.For(await db.Set<StudentProfile>().SingleAsync(p => p.Id == student.Id), guardian, "Father", true));
            }

            await db.SaveChangesAsync();
            students.Add(student.Id);
        }

        var live = await AddLiveSessionAsync(world, imported, null);
        var teacher = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, teacherUser);
        return new Setup(tree, world, teacher, [.. students], live, guardianUser, world.Math.Id);
    }

    private async Task<LiveSession> AddLiveSessionAsync(TimetableWorld world, bool imported, Guid? moduleId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == world.Math.Id);
        var starts = SchoolDay.ToDateTime(new TimeOnly(6, 0), DateTimeKind.Utc); // 09:00 in Riyadh
        var live = new LiveSession(classCourse, Guid.NewGuid(), null, null, "Fake", starts, 60);
        live.Synced(Guid.NewGuid().ToString("N"), null, "join", "start");
        live.MarkLive();
        live.MarkEnded();
        if (imported)
        {
            live.MarkParticipantsImported(DateTime.UtcNow);
        }

        if (moduleId is { } id)
        {
            live.AttachModule(id);
        }

        db.Add(live);
        await db.SaveChangesAsync();
        return live;
    }

    private async Task<LiveSessionParticipant> AddParticipantAsync(Setup s, string name, string state, Guid? student, int fromMinute, int toMinute)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.ClassCourseId);
        var live = await db.Set<LiveSession>().SingleAsync(x => x.Id == s.Live.Id);
        var row = new LiveSessionParticipant(
            live, classCourse, name, state, student, live.StartsAtUtc.AddMinutes(fromMinute), live.StartsAtUtc.AddMinutes(toMinute), (toMinute - fromMinute) * 60);
        db.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private async Task<AttendanceSession?> ProposalOfAsync(Guid liveId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AttendanceSession>().SingleOrDefaultAsync(a => a.LiveSessionId == liveId);
    }

    private async Task<Dictionary<Guid, string?>> StatusesAsync(Guid attendanceSessionId)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<AttendanceRecord>().Where(r => r.AttendanceSessionId == attendanceSessionId).ToDictionaryAsync(r => r.StudentProfileId, r => r.Status);
    }

    private async Task<AttendanceRecordsDto> GetAsync(RoleAssignment who, Guid sessionId)
    {
        var response = await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionRecords(sessionId), who);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AttendanceRecordsDto>(Web))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Sink_only_enqueues_and_returns()
    {
        var s = await SetUpAsync("LP1");
        RecordingBackgroundJobs.Queue.Clear();
        await using var db = factory.CreateDbContext(TestCurrentContext.For(s.Tree.Org.Id, s.Tree.School1.Path));
        var sink = new LiveAttendanceSink(db, new RecordingBackgroundJobs(TestCurrentContext.PlatformAdmin()));

        await sink.SessionEndedAsync(s.Live.Id, s.ClassCourseId, s.Live.StartsAtUtc, s.Live.EndsAtUtc, [], CancellationToken.None);

        Assert.Equal(typeof(LiveAttendanceJob), Assert.Single(RecordingBackgroundJobs.Queue).Type);
        Assert.Null(await ProposalOfAsync(s.Live.Id)); // the sink wrote nothing itself
    }

    [Fact]
    public async Task The_attendance_sink_is_the_registered_one()
    {
        using var scope = factory.Services.CreateScope();

        Assert.IsType<LiveAttendanceSink>(scope.ServiceProvider.GetRequiredService<ILiveSessionAttendanceSink>());
    }

    [Fact]
    public async Task Proposal_is_created_once_per_live_session()
    {
        var s = await SetUpAsync("LP2");
        await AddParticipantAsync(s, "Matched 0", LiveSessionRules.Matched, s.Students[0], 0, 60);

        var first = await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);
        var second = await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);

        Assert.Equal((true, false), (first, second));
        var proposal = await ProposalOfAsync(s.Live.Id); // SingleOrDefault: two would throw
        Assert.Equal((AttendanceSession.LiveSource, AttendanceSession.OpenState, SchoolDay), (proposal!.Source, proposal.State, proposal.Date));
        Assert.Equal(3, (await StatusesAsync(proposal.Id)).Count);
    }

    [Fact]
    public async Task No_proposal_until_participants_are_imported()
    {
        var s = await SetUpAsync("LP3", imported: false);

        var created = await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);

        Assert.False(created);
        Assert.Null(await ProposalOfAsync(s.Live.Id)); // nobody is proposed Absent for a session nobody could read
    }

    [Fact]
    public async Task Only_matched_participants_are_proposed_present_and_guests_are_never_attached()
    {
        var s = await SetUpAsync("LP4");
        await AddParticipantAsync(s, "Matched 0", LiveSessionRules.Matched, s.Students[0], 0, 60);
        await AddParticipantAsync(s, "Matched 1 late", LiveSessionRules.Matched, s.Students[1], 25, 60); // joined after the late limit
        await AddParticipantAsync(s, "Student Two", LiveSessionRules.Guest, null, 0, 60); // looks like student 2 by name: never matched by name

        await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);

        var statuses = await StatusesAsync((await ProposalOfAsync(s.Live.Id))!.Id);
        Assert.Equal([AttendanceStatuses.Present, AttendanceStatuses.Late, AttendanceStatuses.Absent], s.Students.Select(id => statuses[id]));
    }

    [Fact]
    public async Task Hosts_and_staff_are_ignored_and_only_unmatched_guests_are_listed_for_mapping()
    {
        var s = await SetUpAsync("LP5");
        var outsider = await PeopleTestKit.AddStudentAsync(factory, s.Tree.School1, PeopleTestKit.NewCode()); // a student of the school who is not on this roster
        await AddParticipantAsync(s, "Other class student", LiveSessionRules.Matched, outsider.Id, 0, 60);
        var guest = await AddParticipantAsync(s, "Shared tablet", LiveSessionRules.Guest, null, 5, 50);
        await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);
        var proposal = (await ProposalOfAsync(s.Live.Id))!;

        var view = await GetAsync(s.Teacher, proposal.Id);

        Assert.DoesNotContain(view.Records, r => r.StudentId == outsider.Id); // not on the roster: no record
        var unmatched = Assert.Single(view.Unmatched);
        Assert.Equal((guest.Id, "Shared tablet", 45, null), (unmatched.ParticipantId, unmatched.DisplayName, unmatched.JoinedMinutes, unmatched.MappedStudentId));
        Assert.True(view.ParticipantsAvailable);
        Assert.All(view.Records, r => Assert.True(r.Proposed)); // still the server proposal
        Assert.True(view.Session.IsProposal);
    }

    [Fact]
    public async Task Roster_follows_the_group_target()
    {
        var s = await SetUpAsync("LP6");
        Guid moduleId;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.ClassCourseId);
            var group = new StudentGroup(classCourse, "مجموعة", "Group A", null, null);
            var enrollment = await db.Set<CourseEnrollment>().FirstAsync(e => e.ClassCourseId == s.ClassCourseId && e.StudentId == s.Students[1]);
            var plan = new ClassLessonPlan(classCourse, null, null, null);
            var section = new ClassSection(plan, Guid.NewGuid(), 1, "قسم", "Section");
            var lesson = new ClassLesson(section, Guid.NewGuid(), 1, "درس", "Lesson", null, null, null);
            var module = new LessonModule(classCourse, lesson, ModuleKinds.LiveSession, 1);
            module.Apply("ص", "Live", ModuleVisibilities.Published, null, null, ModuleTargets.Group, group.Id, CompletionRules.None, null);
            db.AddRange(group, new GroupMember(group, enrollment), plan, section, lesson, module);
            await db.SaveChangesAsync();
            moduleId = module.Id;
        }

        var grouped = await AddLiveSessionAsync(s.World, true, moduleId);

        await Proposer().ProposeAsync(s.Tree.Org.Id, grouped.Id, CancellationToken.None);

        var statuses = await StatusesAsync((await ProposalOfAsync(grouped.Id))!.Id);
        Assert.Equal([s.Students[1]], statuses.Keys); // only the targeted group is on the proposal
    }

    [Fact]
    public async Task Live_attendance_job_with_a_foreign_org_payload_does_nothing()
    {
        var s = await SetUpAsync("LP7");
        var other = await AccessTestData.SeedAsync(factory, "LP7B");

        var created = await Proposer().ProposeAsync(other.Org.Id, s.Live.Id, CancellationToken.None);

        Assert.False(created); // the session is not in that client: the payload client is re-checked against the row
        Assert.Null(await ProposalOfAsync(s.Live.Id));
    }

    [Fact]
    public async Task Reconcile_sweep_creates_a_missed_proposal_and_runs_each_client_in_its_own_context()
    {
        var a = await SetUpAsync("LP8");
        var b = await SetUpAsync("LP9");
        var notReady = await SetUpAsync("LP10", imported: false);

        var created = await Proposer().ReconcileAllAsync(CancellationToken.None);

        Assert.True(created >= 2);
        Assert.NotNull(await ProposalOfAsync(a.Live.Id));
        Assert.NotNull(await ProposalOfAsync(b.Live.Id));
        Assert.Null(await ProposalOfAsync(notReady.Live.Id)); // participants not imported: never proposed
        Assert.Equal(0, await Proposer().ReconcileAsync(a.Tree.Org.Id, CancellationToken.None)); // idempotent
    }

    [Fact]
    public async Task Mapping_is_stored_on_the_attendance_side_and_reproposes_only_that_student()
    {
        var s = await SetUpAsync("LP11");
        await AddParticipantAsync(s, "Matched 0", LiveSessionRules.Matched, s.Students[0], 0, 60);
        var guest = await AddParticipantAsync(s, "Tablet", LiveSessionRules.Guest, null, 0, 60);
        await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);
        var proposal = (await ProposalOfAsync(s.Live.Id))!;
        var before = await GetAsync(s.Teacher, proposal.Id);
        Assert.Equal(AttendanceStatuses.Absent, before.Records.Single(r => r.StudentId == s.Students[2]).Status);

        var response = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionLiveMappings(proposal.Id), s.Teacher,
            new LiveMappingRequest([new LiveMappingEntry(guest.Id, s.Students[2])], before.Session.Version));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = (await response.Content.ReadFromJsonAsync<AttendanceRecordsDto>(Web))!;
        Assert.Equal(AttendanceStatuses.Present, after.Records.Single(r => r.StudentId == s.Students[2]).Status);
        Assert.Equal(s.Students[2], Assert.Single(after.Unmatched).MappedStudentId);
        Assert.Equal(before.Session.Version + 1, after.Session.Version);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(s.Students[2], (await db.Set<LiveAttendanceMapping>().SingleAsync(m => m.AttendanceSessionId == proposal.Id)).StudentProfileId);
        Assert.Null((await db.Set<LiveSessionParticipant>().SingleAsync(p => p.Id == guest.Id)).StudentId); // the live module row is untouched (L11 item 8)
    }

    [Fact]
    public async Task Mapping_refuses_a_matched_row_a_stranger_and_another_teacher()
    {
        var s = await SetUpAsync("LP12");
        var matched = await AddParticipantAsync(s, "Matched 0", LiveSessionRules.Matched, s.Students[0], 0, 60);
        var guest = await AddParticipantAsync(s, "Tablet", LiveSessionRules.Guest, null, 0, 60);
        await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);
        var proposal = (await ProposalOfAsync(s.Live.Id))!;
        var version = (await GetAsync(s.Teacher, proposal.Id)).Session.Version;
        var otherTeacher = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Teacher, (await PeopleTestKit.AddUserAsync(factory)).Id);

        var onMatched = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionLiveMappings(proposal.Id), s.Teacher,
            new LiveMappingRequest([new LiveMappingEntry(matched.Id, s.Students[1])], version));
        var toStranger = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionLiveMappings(proposal.Id), s.Teacher,
            new LiveMappingRequest([new LiveMappingEntry(guest.Id, Guid.NewGuid())], version));
        var asOther = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionLiveMappings(proposal.Id), otherTeacher,
            new LiveMappingRequest([new LiveMappingEntry(guest.Id, s.Students[1])], version));
        var stale = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionLiveMappings(proposal.Id), s.Teacher,
            new LiveMappingRequest([], version + 5));

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.Conflict],
            [onMatched.StatusCode, toStranger.StatusCode, asOther.StatusCode, stale.StatusCode]);
    }

    [Fact]
    public async Task Guardians_are_notified_on_confirm_not_on_proposal_and_a_cancelled_live_session_keeps_taken_attendance()
    {
        var s = await SetUpAsync("LP13");
        await AddParticipantAsync(s, "Matched 1", LiveSessionRules.Matched, s.Students[1], 0, 60);
        await AddParticipantAsync(s, "Matched 2", LiveSessionRules.Matched, s.Students[2], 0, 60); // student 0 (the one with a guardian) stays Absent
        await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);
        var proposal = (await ProposalOfAsync(s.Live.Id))!;
        async Task<int> NoticesAsync()
        {
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            return await db.Set<Notification>().CountAsync(n => n.UserId == s.GuardianUser && n.EventKey == NotificationEvents.AttendanceAbsent);
        }

        Assert.Equal(0, await NoticesAsync()); // the proposal told nobody
        var open = await GetAsync(s.Teacher, proposal.Id);
        var edit = await SendAsync(factory, HttpMethod.Put, Routes.AttendanceSessionRecords(proposal.Id), s.Teacher,
            new TakeAttendanceRequest(null, [new TakeAttendanceRecord(s.Students[0], AttendanceStatuses.Absent, null), new TakeAttendanceRecord(s.Students[1], AttendanceStatuses.Present, null),
                new TakeAttendanceRecord(s.Students[2], AttendanceStatuses.Present, null)], null, open.Session.Version));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(0, await NoticesAsync()); // saving an edit does not take the proposal either
        var edited = (await edit.Content.ReadFromJsonAsync<AttendanceRecordsDto>(Web))!;
        Assert.Equal((AttendanceSessionStates.Open, true), (edited.Session.State, edited.Session.IsProposal));

        var confirm = await SendAsync(factory, HttpMethod.Post, Routes.AttendanceSessionConfirm(proposal.Id), s.Teacher, new ConfirmAttendanceRequest(edited.Session.Version));

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var taken = (await confirm.Content.ReadFromJsonAsync<AttendanceRecordsDto>(Web))!;
        Assert.Equal((AttendanceSessionStates.Taken, false), (taken.Session.State, taken.Session.IsProposal));
        Assert.Equal(1, await NoticesAsync()); // now the family is told, once
        var again = await SendAsync(factory, HttpMethod.Post, Routes.AttendanceSessionConfirm(proposal.Id), s.Teacher, new ConfirmAttendanceRequest(taken.Session.Version));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.attendance_not_a_proposal), (again.StatusCode, await CodeAsync(again)));
        Assert.Equal(1, await NoticesAsync());

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<LiveSession>().SingleAsync(x => x.Id == s.Live.Id)).Cancel(); // item 9: no cascade, a taken session stays valid
            await db.SaveChangesAsync();
        }

        var later = await GetAsync(s.Teacher, proposal.Id);
        Assert.Equal(AttendanceSessionStates.Taken, later.Session.State);
        Assert.Equal(AttendanceStatuses.Absent, later.Records.Single(r => r.StudentId == s.Students[0]).Status);
        var history = (await (await SendAsync(factory, HttpMethod.Get, Routes.AttendanceSessionHistory(proposal.Id), s.Teacher)).Content.ReadFromJsonAsync<List<AttendanceChangeDto>>(Web))!;
        Assert.Equal(3, history.Count);
        Assert.All(history, h => Assert.Equal(AttendanceChangeKinds.Taken, h.Kind));
    }

    private async Task<LessonModule> AddLiveModuleAsync(Setup s, LiveSession live)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.ClassCourseId);
        var plan = new ClassLessonPlan(classCourse, null, null, null);
        var section = new ClassSection(plan, Guid.NewGuid(), 1, "قسم", "Section");
        var lesson = new ClassLesson(section, live.LessonKey, 1, "درس", "Lesson", null, null, null);
        var module = new LessonModule(classCourse, lesson, ModuleKinds.LiveSession, 1);
        module.Apply("ص", "Live", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.None, null);
        module.SetRef(live.Id);
        db.AddRange(plan, section, lesson, module);
        await db.SaveChangesAsync();
        return module;
    }

    [Fact]
    public async Task The_live_session_list_links_the_proposal_for_staff_and_never_for_a_student()
    {
        var s = await SetUpAsync("LP14");
        await AddLiveModuleAsync(s, s.Live);
        await Proposer().ProposeAsync(s.Tree.Org.Id, s.Live.Id, CancellationToken.None);
        var proposal = (await ProposalOfAsync(s.Live.Id))!;
        var studentUser = await PeopleTestKit.AddUserAsync(factory);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<StudentProfile>().SingleAsync(p => p.Id == s.Students[0])).LinkUser(studentUser.Id);
            await db.SaveChangesAsync();
        }

        var student = await AccessTestData.AssignAsync(factory, s.Tree, s.Tree.School1, BuiltInRoles.Student, studentUser.Id);

        var staffList = await SendAsync(factory, HttpMethod.Get, Routes.ClassCourseLiveSessions(s.ClassCourseId), s.Teacher);
        var studentList = await SendAsync(factory, HttpMethod.Get, Routes.ClassCourseLiveSessions(s.ClassCourseId), student);

        Assert.Equal(proposal.Id, (await staffList.Content.ReadFromJsonAsync<PagedResult<Lms.Shared.LiveSessions.LiveSessionListItemDto>>(Web))!.Items.Single().AttendanceSessionId);
        var studentRows = (await studentList.Content.ReadFromJsonAsync<PagedResult<Lms.Shared.LiveSessions.LiveSessionListItemDto>>(Web))!.Items;
        Assert.Equal(s.Live.Id, studentRows.Single().Id);
        Assert.Null(studentRows.Single().AttendanceSessionId); // a student never learns whether a proposal exists
    }

    [Fact]
    public async Task The_timetable_card_carries_the_live_session_and_the_join_time_but_not_for_a_cancelled_one()
    {
        var s = await SetUpAsync("LP15");
        var sunday = s.World.Term.StartDate;
        while (sunday.DayOfWeek != DayOfWeek.Sunday)
        {
            sunday = sunday.AddDays(1);
        }

        LiveSession live;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var slot = new Lms.Domain.Timetable.TimetableSlot(s.World.Class, s.ClassCourseId, s.World.Term.Id, s.Teacher.UserId, DayOfWeek.Sunday, s.World.First.Id, null);
            slot.Publish();
            var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.ClassCourseId);
            live = new LiveSession(classCourse, Guid.NewGuid(), null, null, "Fake", sunday.ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc), 45); // 08:00 in Riyadh
            live.Synced(Guid.NewGuid().ToString("N"), null, "join", "start");
            db.AddRange(slot, live);
            await db.SaveChangesAsync();
        }

        async Task<Lms.Shared.Timetable.TimetableCardDto> CardAsync()
        {
            var response = await SendAsync(factory, HttpMethod.Get, Routes.MyTimetable(sunday, sunday), s.Teacher);
            return (await response.Content.ReadFromJsonAsync<Lms.Shared.Timetable.TimetableViewDto>(Web))!.Cards.Single();
        }

        var card = await CardAsync();
        Assert.Equal((live.Id, live.StartsAtUtc.AddMinutes(-15)), (card.LiveSessionId, card.JoinOpensAtUtc)); // the default join window is 15 minutes

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<LiveSession>().SingleAsync(x => x.Id == live.Id)).Cancel();
            await db.SaveChangesAsync();
        }

        Assert.Null((await CardAsync()).LiveSessionId);
    }
}
