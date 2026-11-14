using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Messaging;
using Lms.Domain.People;
using Lms.Domain.Teaching;
using Lms.Infrastructure;
using Lms.Infrastructure.Messaging.Policy;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.AcademicYears.YearTestKit;

namespace Lms.IntegrationTests.Messaging;

/// <summary>
/// The D69 safeguarding policy in the real database (M6 Task 11, FR-MSG-02): the default matrix by role pair and relationship, narrowing only, the reply path, blocks,
/// restrictions, recipient search and the client boundary. Role pairs are asked through <see cref="IMessagingPolicy"/> and through the HTTP routes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MessagingPolicyTests(LmsApiFactory factory)
{
    private sealed class World
    {
        public required AccessTree Tree { get; init; }
        public required ClassCourse Math { get; init; }
        public required ClassCourse Science { get; init; }
        public required Guid MathTeacher { get; init; }
        public required Guid ScienceTeacher { get; init; }
        public required Guid Counselor { get; init; }
        public required Guid OtherSchoolCounselor { get; init; }
        public required Guid Manager { get; init; }
        public required Guid OrgAdmin { get; init; }
        public required Guid StudentA { get; init; }
        public required Guid StudentB { get; init; }
        public required Guid GuardianA { get; init; }
        public required Guid GuardianB { get; init; }
        public required Guid StudentAProfile { get; init; }
        public required RoleAssignment OrgAdminCaller { get; init; }
        public required RoleAssignment SchoolAdminCaller { get; init; }
        public required RoleAssignment StudentCaller { get; init; }
        public required RoleAssignment GuardianCaller { get; init; }
        public required RoleAssignment TeacherCaller { get; init; }
    }

    private static readonly DateOnly Start = new(2020, 1, 1);

    private MessagingPolicy PolicyIn(AccessTree tree, LmsApiFactory f, out Lms.Infrastructure.Persistence.LmsDbContext db)
    {
        db = f.CreateDbContext(TestCurrentContext.For(tree.Org.Id, tree.Root.Path));
        return new MessagingPolicy(db, new RelationshipLookup(db), new SystemClock(), TestCurrentContext.For(tree.Org.Id, tree.Root.Path));
    }

    private async Task<World> SetUpAsync(string name)
    {
        var tree = await AccessTestData.SeedAsync(factory, name);
        var year = await AcademicTestKit.AddYearAsync(factory, tree.School1);
        var cls = await AcademicTestKit.AddClassAsync(factory, tree.School1, year);
        var mathCourse = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var scienceCourse = await AcademicTestKit.AddCourseAsync(factory, tree.Root);
        var math = await AcademicTestKit.AddClassCourseAsync(factory, cls, mathCourse);
        var science = await AcademicTestKit.AddClassCourseAsync(factory, cls, scienceCourse);
        var mathTeacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var scienceTeacher = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var studentAUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var studentBUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianAUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var guardianBUser = (await PeopleTestKit.AddUserAsync(factory)).Id;
        Guid studentAProfile;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new ClassCourseTeacher(await db.Set<ClassCourse>().SingleAsync(c => c.Id == math.Id), mathTeacher, ClassCourseTeacher.PrimaryRole, Start));
            db.Add(new ClassCourseTeacher(await db.Set<ClassCourse>().SingleAsync(c => c.Id == science.Id), scienceTeacher, ClassCourseTeacher.PrimaryRole, Start));
            await db.SaveChangesAsync();
        }

        // Student A takes Math only; student B takes Science only (so each has exactly one teacher).
        async Task<Guid> EnrollAsync(ClassCourse classCourse, Guid studentUser, Guid guardianUser)
        {
            var student = await PeopleTestKit.AddStudentAsync(factory, tree.School1, PeopleTestKit.NewCode());
            await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
            var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.LinkUser(studentUser);
            var placement = new ClassStudent(cls, student.Id, Start);
            db.Add(placement);
            db.Add(new CourseEnrollment(await db.Set<ClassCourse>().SingleAsync(c => c.Id == classCourse.Id), placement, "Auto", Start));
            var guardian = new GuardianProfile(tree.Org.Id, "ولي", "Guardian", PeopleTestKit.NewMobile(), null, null);
            guardian.LinkUser(guardianUser);
            db.Add(guardian);
            db.Add(StudentGuardian.For(tracked, guardian, "Father", true));
            await db.SaveChangesAsync();
            return student.Id;
        }

        studentAProfile = await EnrollAsync(math, studentAUser, guardianAUser);
        await EnrollAsync(science, studentBUser, guardianBUser);

        var counselor = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var otherCounselor = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var manager = (await PeopleTestKit.AddUserAsync(factory)).Id;
        var orgAdmin = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Counselor, counselor);
        await AccessTestData.AssignAsync(factory, tree, tree.School2, BuiltInRoles.Counselor, otherCounselor);
        await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.Manager, manager);
        var orgAdminCaller = await AccessTestData.AssignAsync(factory, tree, tree.Root, BuiltInRoles.OrgAdmin, orgAdmin);
        return new World
        {
            Tree = tree, Math = math, Science = science, MathTeacher = mathTeacher, ScienceTeacher = scienceTeacher, Counselor = counselor, OtherSchoolCounselor = otherCounselor,
            Manager = manager, OrgAdmin = orgAdmin, StudentA = studentAUser, StudentB = studentBUser, GuardianA = guardianAUser, GuardianB = guardianBUser,
            StudentAProfile = studentAProfile, OrgAdminCaller = orgAdminCaller,
            SchoolAdminCaller = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id),
            StudentCaller = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Student, studentAUser),
            GuardianCaller = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Guardian, guardianAUser),
            TeacherCaller = await AccessTestData.AssignAsync(factory, tree, tree.School1, BuiltInRoles.Teacher, mathTeacher),
        };
    }

    private async Task AssignAllAsync(World w)
    {
        // The students, guardians and teachers hold their built-in roles (assignments are what the policy reads); the callers above already did this for student A, guardian A and the Math teacher.
        await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Student, w.StudentB);
        await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Guardian, w.GuardianB);
        await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Teacher, w.ScienceTeacher);
    }

    private async Task<World> FullWorldAsync(string name)
    {
        var w = await SetUpAsync(name);
        await AssignAllAsync(w);
        return w;
    }

    private async Task<bool> CanStartAsync(World w, Guid from, Guid to)
    {
        var policy = PolicyIn(w.Tree, factory, out var db);
        await using var _ = db;
        return (await policy.CanStartAsync(from, to)).Allowed;
    }

    [Fact]
    public async Task Student_cannot_message_another_student_and_reaches_own_teachers_and_counselor_only()
    {
        var w = await FullWorldAsync("MP1");

        Assert.False(await CanStartAsync(w, w.StudentA, w.StudentB));
        Assert.True(await CanStartAsync(w, w.StudentA, w.MathTeacher)); // own class course teacher
        Assert.False(await CanStartAsync(w, w.StudentA, w.ScienceTeacher)); // a teacher of a course the student does not take
        Assert.True(await CanStartAsync(w, w.StudentA, w.Counselor)); // the counselor of their school
        Assert.False(await CanStartAsync(w, w.StudentA, w.OtherSchoolCounselor)); // another school
        Assert.False(await CanStartAsync(w, w.StudentA, w.Manager));
        Assert.False(await CanStartAsync(w, w.StudentA, w.GuardianA)); // not even their own guardian: the matrix has no Student to Guardian cell
    }

    [Fact]
    public async Task Guardian_cannot_message_another_guardian_and_reaches_only_own_childrens_teachers_and_counselor()
    {
        var w = await FullWorldAsync("MP2");

        Assert.False(await CanStartAsync(w, w.GuardianA, w.GuardianB));
        Assert.True(await CanStartAsync(w, w.GuardianA, w.MathTeacher));
        Assert.False(await CanStartAsync(w, w.GuardianA, w.ScienceTeacher)); // teaches only someone else's child
        Assert.True(await CanStartAsync(w, w.GuardianA, w.Counselor));
        Assert.False(await CanStartAsync(w, w.GuardianA, w.OtherSchoolCounselor));
        Assert.False(await CanStartAsync(w, w.GuardianA, w.StudentA));
    }

    [Fact]
    public async Task Managers_and_admins_reach_staff_and_guardians_but_never_open_a_private_conversation_with_a_student()
    {
        var w = await FullWorldAsync("MP3");

        Assert.False(await CanStartAsync(w, w.OrgAdmin, w.StudentA));
        Assert.False(await CanStartAsync(w, w.Manager, w.StudentA));
        Assert.True(await CanStartAsync(w, w.Manager, w.MathTeacher));
        Assert.True(await CanStartAsync(w, w.Manager, w.GuardianA));
        Assert.True(await CanStartAsync(w, w.OrgAdmin, w.Counselor));
        Assert.True(await CanStartAsync(w, w.MathTeacher, w.StudentA)); // a teacher reaches their own student
        Assert.False(await CanStartAsync(w, w.MathTeacher, w.StudentB)); // not another teacher student
        Assert.True(await CanStartAsync(w, w.MathTeacher, w.GuardianA));
        Assert.False(await CanStartAsync(w, w.MathTeacher, w.GuardianB));
    }

    [Fact]
    public async Task Former_teacher_loses_the_relationship_after_the_end_date()
    {
        var w = await FullWorldAsync("MP4");
        Assert.True(await CanStartAsync(w, w.StudentA, w.MathTeacher));

        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var assignment = await db.Set<ClassCourseTeacher>().SingleAsync(t => t.UserId == w.MathTeacher);
            assignment.End(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));
            await db.SaveChangesAsync();
        }

        Assert.False(await CanStartAsync(w, w.StudentA, w.MathTeacher));
        Assert.False(await CanStartAsync(w, w.MathTeacher, w.StudentA));
        Assert.False(await CanStartAsync(w, w.GuardianA, w.MathTeacher));
    }

    [Fact]
    public async Task A_user_with_two_roles_is_judged_pair_by_pair_and_the_best_pair_wins_never_a_mix()
    {
        var w = await FullWorldAsync("MP5");
        // The Math teacher is also an Org Admin: towards students they reach only their own (as a teacher), not every student (an Org Admin cannot write to a student).
        await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, w.MathTeacher);

        Assert.True(await CanStartAsync(w, w.MathTeacher, w.StudentA));
        Assert.False(await CanStartAsync(w, w.MathTeacher, w.StudentB));
    }

    [Fact]
    public async Task Guardian_can_reply_to_a_manager_who_wrote_first_but_cannot_start_one_and_tightening_stops_the_reply_path_until_restored()
    {
        var w = await FullWorldAsync("MP6");

        Assert.True(await CanStartAsync(w, w.Manager, w.GuardianA)); // the manager may write first
        Assert.False(await CanStartAsync(w, w.GuardianA, w.Manager)); // the guardian may not start one

        var tighten = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Deny));
        Assert.Equal(HttpStatusCode.OK, tighten.StatusCode);
        Assert.False(await CanStartAsync(w, w.Manager, w.GuardianA)); // new conversations stop
        var policy = PolicyIn(w.Tree, factory, out var db);
        await using (db)
        {
            // The creating rule (manager to guardian) no longer holds: an existing conversation becomes read-only for both with reason rule.
            Assert.Equal(MessagingReasons.Rule, (await policy.CreatingRuleHoldsAsync(w.Manager, w.GuardianA)).Reason);
        }

        var restore = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Allow));
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        var again = PolicyIn(w.Tree, factory, out var db2);
        await using (db2)
        {
            Assert.True((await again.CreatingRuleHoldsAsync(w.Manager, w.GuardianA)).Allowed); // restoring the rule restores posting
        }
    }

    private Task<HttpResponseMessage> PutRulesAsync(World w, params MessagingRuleCellDto[] cells) =>
        SendAsync(factory, HttpMethod.Put, Routes.CommsMessagingRules, w.OrgAdminCaller, new SaveMessagingRulesRequest(cells));

    [Fact]
    public async Task Rules_cannot_be_loosened_below_the_defaults_cell_by_cell_and_the_request_is_refused_whole()
    {
        var w = await FullWorldAsync("MP7");
        var loosened = 0;
        foreach (var from in MessagingRuleDefaults.Roles)
        {
            foreach (var to in MessagingRuleDefaults.Roles)
            {
                var def = MessagingRuleDefaults.Default(from, to);
                foreach (var mode in MessagingRuleModes.All.Where(m => MessagingRuleModes.Rank(m) > MessagingRuleModes.Rank(def)))
                {
                    var response = await PutRulesAsync(w, new MessagingRuleCellDto(from, to, mode));
                    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                    Assert.Equal(ProblemCodes.rules_looser_than_default, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
                    loosened++;
                }
            }
        }

        Assert.True(loosened > 40); // every cell that has a looser mode above its default was tried
        // A request that mixes one narrowing with one widening is refused whole: nothing is saved.
        var mixed = await PutRulesAsync(w,
            new MessagingRuleCellDto(BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Deny),
            new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Student, MessagingRuleModes.Allow));
        Assert.Equal(HttpStatusCode.BadRequest, mixed.StatusCode);
        Assert.True(await CanStartAsync(w, w.Manager, w.GuardianA));
        // An unknown role or mode is refused too.
        Assert.Equal(HttpStatusCode.BadRequest, (await PutRulesAsync(w, new MessagingRuleCellDto("Wizard", BuiltInRoles.Student, MessagingRuleModes.Deny))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, "Everyone"))).StatusCode);
    }

    [Fact]
    public async Task Widening_an_allowed_cell_is_refused_and_a_tightening_with_a_restore_is_accepted()
    {
        var w = await FullWorldAsync("MP8");

        var widen = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.SameUnit));
        var allow = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Allow));
        var tighten = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Deny));
        var restore = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Teacher, BuiltInRoles.Student, MessagingRuleModes.Relationship));

        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (widen.StatusCode, allow.StatusCode));
        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (tighten.StatusCode, restore.StatusCode));
        var rules = (await (await SendAsync(factory, HttpMethod.Get, Routes.CommsMessagingRules, w.OrgAdminCaller)).Content.ReadFromJsonAsync<MessagingRulesDto>(Web))!;
        Assert.Equal(MessagingRuleModes.Relationship, rules.Cells.Single(c => c.FromRole == BuiltInRoles.Teacher && c.ToRole == BuiltInRoles.Student).Mode);
        Assert.Equal(MessagingRuleDefaults.Matrix().Count, rules.Defaults.Count);
    }

    [Fact]
    public async Task Policy_ignores_a_stored_rule_looser_than_the_default()
    {
        var w = await FullWorldAsync("MP9");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new MessagingRule(w.Tree.Org.Id, BuiltInRoles.Student, BuiltInRoles.Student, MessagingRuleModes.Allow)); // a hand-edited row
            db.Add(new MessagingRule(w.Tree.Org.Id, BuiltInRoles.Guardian, BuiltInRoles.Guardian, MessagingRuleModes.SameUnit));
            await db.SaveChangesAsync();
        }

        Assert.False(await CanStartAsync(w, w.StudentA, w.StudentB));
        Assert.False(await CanStartAsync(w, w.GuardianA, w.GuardianB));
        var shown = (await (await SendAsync(factory, HttpMethod.Get, Routes.CommsMessagingRules, w.OrgAdminCaller)).Content.ReadFromJsonAsync<MessagingRulesDto>(Web))!;
        Assert.Equal(MessagingRuleModes.Deny, shown.Cells.Single(c => c.FromRole == BuiltInRoles.Student && c.ToRole == BuiltInRoles.Student).Mode); // and the read shows what counts
    }

    [Fact]
    public async Task Rules_are_for_the_client_root_only()
    {
        var w = await FullWorldAsync("MP10");

        var below = await SendAsync(factory, HttpMethod.Get, Routes.CommsMessagingRules, w.SchoolAdminCaller);
        var put = await PutRulesAsync(w, new MessagingRuleCellDto(BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Allow));
        var asTeacher = await SendAsync(factory, HttpMethod.Get, Routes.CommsMessagingRules, w.TeacherCaller);
        var asStudent = await SendAsync(factory, HttpMethod.Put, Routes.CommsMessagingRules, w.StudentCaller, new SaveMessagingRulesRequest([]));

        Assert.Equal(HttpStatusCode.Forbidden, below.StatusCode);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (asTeacher.StatusCode, asStudent.StatusCode));
    }

    [Fact]
    public async Task A_block_stops_direct_conversations_both_ways_is_neutral_and_leaves_groups_alone()
    {
        var w = await FullWorldAsync("MP11");
        Assert.True(await CanStartAsync(w, w.StudentA, w.MathTeacher));

        var blocked = await SendAsync(factory, HttpMethod.Put, Routes.MyBlock(w.MathTeacher), w.StudentCaller);

        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.False(await CanStartAsync(w, w.StudentA, w.MathTeacher)); // the blocker cannot start
        Assert.False(await CanStartAsync(w, w.MathTeacher, w.StudentA)); // and the blocked person cannot either
        var policy = PolicyIn(w.Tree, factory, out var db);
        await using (db)
        {
            var viaBlock = await policy.CanStartAsync(w.MathTeacher, w.StudentA);
            var viaRule = await policy.CanStartAsync(w.StudentA, w.StudentB);
            Assert.Equal(MessagingReasons.BlockedOrRule, viaBlock.Reason); // the same neutral reason: a block is never revealed
            Assert.Equal(viaBlock.Reason, viaRule.Reason);
            Assert.True((await policy.CanAddToGroupAsync(w.MathTeacher, w.StudentA)).Allowed); // groups are unchanged
            Assert.True(await policy.IsBlockedBetweenAsync(w.StudentA, w.MathTeacher));
        }

        var mine = (await (await SendAsync(factory, HttpMethod.Get, Routes.MyBlocks, w.StudentCaller)).Content.ReadFromJsonAsync<List<BlockDto>>(Web))!;
        Assert.Equal(w.MathTeacher, Assert.Single(mine).UserId);
        var theirs = (await (await SendAsync(factory, HttpMethod.Get, Routes.MyBlocks, w.TeacherCaller)).Content.ReadFromJsonAsync<List<BlockDto>>(Web))!;
        Assert.Empty(theirs); // the blocked person never sees it

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Put, Routes.MyBlock(w.MathTeacher), w.StudentCaller)).StatusCode); // blocking twice is fine
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(factory, HttpMethod.Delete, Routes.MyBlock(w.MathTeacher), w.StudentCaller)).StatusCode);
        Assert.True(await CanStartAsync(w, w.StudentA, w.MathTeacher));
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, Routes.MyBlock(w.StudentB), w.StudentCaller)).StatusCode); // someone the caller could not message: nothing is confirmed
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(factory, HttpMethod.Put, Routes.MyBlock(Guid.NewGuid()), w.StudentCaller)).StatusCode); // an unknown user: the same answer
    }

    [Fact]
    public async Task A_restricted_user_cannot_send_until_the_end()
    {
        var w = await FullWorldAsync("MP12");
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new MessagingRestriction(w.Tree.Org.Id, w.StudentA, DateTime.UtcNow.AddHours(2)));
            db.Add(new MessagingRestriction(w.Tree.Org.Id, w.StudentB, DateTime.UtcNow.AddHours(-2))); // already over
            db.Add(new MessagingRestriction(w.Tree.Org.Id, w.GuardianA, null)); // until lifted
            await db.SaveChangesAsync();
        }

        var policy = PolicyIn(w.Tree, factory, out var read);
        await using (read)
        {
            Assert.True(await policy.IsRestrictedAsync(w.StudentA));
            Assert.False(await policy.IsRestrictedAsync(w.StudentB));
            Assert.True(await policy.IsRestrictedAsync(w.GuardianA));
            Assert.False(await policy.IsRestrictedAsync(w.MathTeacher));
        }
    }

    [Fact]
    public async Task Recipient_search_returns_only_allowed_users_never_enumerates_and_stays_in_the_client()
    {
        var w = await FullWorldAsync("MP13");
        var other = await AccessTestData.SeedAsync(factory, "MP13B");
        await SetNamesAsync(w.MathTeacher, "Mona Maths", "منى الرياضيات");
        await SetNamesAsync(w.ScienceTeacher, "Mona Science", "منى العلوم");
        await SetNamesAsync(w.StudentB, "Mona Classmate", "منى زميلة");
        var stranger = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await SetNamesAsync(stranger, "Mona Elsewhere", "منى أخرى");
        await AccessTestData.AssignAsync(factory, other, other.School1, BuiltInRoles.Teacher, stranger);

        async Task<List<RecipientDto>> SearchAsync(RoleAssignment who, string text)
        {
            var response = await SendAsync(factory, HttpMethod.Get, Routes.MessagingRecipientsSearch(text), who);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<List<RecipientDto>>(Web))!;
        }

        var asStudent = await SearchAsync(w.StudentCaller, "Mona");
        Assert.Equal([w.MathTeacher], asStudent.Select(r => r.UserId)); // own teacher only: not the science teacher, not the classmate, nobody from another client
        Assert.Equal(BuiltInRoles.Teacher, asStudent[0].RoleKey);
        Assert.Empty(await SearchAsync(w.StudentCaller, "Classmate")); // a classmate by name: exactly the answer of no match
        Assert.Empty(await SearchAsync(w.StudentCaller, "zzzzzz"));
        Assert.Empty(await SearchAsync(w.StudentCaller, "M")); // under the minimum length
        var asManager = await SearchAsync(w.OrgAdminCaller, "Mona");
        Assert.Contains(w.MathTeacher, asManager.Select(r => r.UserId));
        Assert.DoesNotContain(w.StudentB, asManager.Select(r => r.UserId)); // an Org Admin never gets a private line to a student
        Assert.DoesNotContain(stranger, asManager.Select(r => r.UserId)); // never another client
    }

    private async Task SetNamesAsync(Guid userId, string en, string ar)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.DisplayNameEn = en;
        user.DisplayNameAr = ar;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Policy_lookup_never_crosses_clients_and_the_summary_and_limits_answer_the_caller()
    {
        var w = await FullWorldAsync("MP14");
        var other = await AccessTestData.SeedAsync(factory, "MP14B");
        var teacherElsewhere = (await PeopleTestKit.AddUserAsync(factory)).Id;
        await AccessTestData.AssignAsync(factory, other, other.School1, BuiltInRoles.Teacher, teacherElsewhere);

        var policy = PolicyIn(w.Tree, factory, out var db);
        await using (db)
        {
            Assert.False((await policy.CanStartAsync(w.StudentA, teacherElsewhere)).Allowed);
            Assert.False((await policy.CanStartAsync(teacherElsewhere, w.StudentA)).Allowed);
        }

        var summary = (await (await SendAsync(factory, HttpMethod.Get, Routes.MessagingRulesSummary, w.StudentCaller)).Content.ReadFromJsonAsync<MessagingRulesSummaryDto>(Web))!;
        Assert.Equal([BuiltInRoles.Teacher, BuiltInRoles.Counselor], summary.ReachableRoleKeys); // students reach teachers and counselors only
        Assert.False(summary.CanCreateGroups);
        var teacherSummary = (await (await SendAsync(factory, HttpMethod.Get, Routes.MessagingRulesSummary, w.TeacherCaller)).Content.ReadFromJsonAsync<MessagingRulesSummaryDto>(Web))!;
        Assert.True(teacherSummary.CanCreateGroups);
        var limits = (await (await SendAsync(factory, HttpMethod.Get, Routes.MessagingLimits, w.StudentCaller)).Content.ReadFromJsonAsync<MessagingLimitsDto>(Web))!;
        Assert.Equal((MessagingLimits.MaxBodyLength, MessagingLimits.DeleteWindowMinutes), (limits.MaxBodyLength, limits.DeleteWindowMinutes));
    }

    [Fact]
    public async Task Messaging_policy_tables_are_isolated_per_client()
    {
        var a = await AccessTestData.SeedAsync(factory, "MPIA");
        var b = await AccessTestData.SeedAsync(factory, "MPIB");
        Guid ruleA, blockA, restrictionA, ruleB, blockB, restrictionB;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var rA = new MessagingRule(a.Org.Id, BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Deny);
            var bA = new UserBlock(a.Org.Id, Guid.NewGuid(), Guid.NewGuid());
            var xA = new MessagingRestriction(a.Org.Id, Guid.NewGuid(), null);
            var rB = new MessagingRule(b.Org.Id, BuiltInRoles.Manager, BuiltInRoles.Guardian, MessagingRuleModes.Deny);
            var bB = new UserBlock(b.Org.Id, Guid.NewGuid(), Guid.NewGuid());
            var xB = new MessagingRestriction(b.Org.Id, Guid.NewGuid(), null);
            db.AddRange(rA, bA, xA, rB, bB, xB);
            await db.SaveChangesAsync();
            (ruleA, blockA, restrictionA, ruleB, blockB, restrictionB) = (rA.Id, bA.Id, xA.Id, rB.Id, bB.Id, xB.Id);
        }

        await using var read = factory.CreateDbContext(TestCurrentContext.For(a.Org.Id, a.Root.Path));
        Assert.Equal([ruleA], await read.Set<MessagingRule>().Where(r => r.Id == ruleA || r.Id == ruleB).Select(r => r.Id).ToListAsync());
        Assert.Equal([blockA], await read.Set<UserBlock>().Where(r => r.Id == blockA || r.Id == blockB).Select(r => r.Id).ToListAsync());
        Assert.Equal([restrictionA], await read.Set<MessagingRestriction>().Where(r => r.Id == restrictionA || r.Id == restrictionB).Select(r => r.Id).ToListAsync());
    }
}
