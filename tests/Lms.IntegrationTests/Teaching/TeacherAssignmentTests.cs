using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Auditing;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Notifications;
using Lms.Domain.Teaching;
using Lms.Infrastructure;
using Lms.Infrastructure.Access;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Classes;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Lms.Shared.Notifications;
using Lms.Shared.Teaching;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Teaching;

[Collection(ApiCollection.Name)]
public sealed class TeacherAssignmentTests(LmsApiFactory factory) : IDisposable
{
    private readonly TeachingTestHost _t = new(factory);
    private static DateOnly Today => TeachingTestHost.Today;

    public void Dispose() => _t.Dispose();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Assigning_eligible_teacher_creates_dated_row_notifies_and_is_audited()
    {
        var s = await _t.SetUpAsync("TA1");
        var teacher = await _t.EligibleTeacherAsync(s);

        var response = await _t.AssignAsync(s, teacher.User.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<ClassCourseTeacherDto>())!;
        Assert.Equal(teacher.User.Id, dto.UserId);
        Assert.Equal(TeacherRoles.Primary, dto.Role);
        Assert.Equal(Today, dto.From);
        Assert.Null(dto.To);
        Assert.True(dto.IsCurrent);
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var notification = await db.Set<Notification>().SingleAsync(n => n.UserId == teacher.User.Id && n.EventKey == NotificationEvents.TeachingAssigned);
        Assert.NotNull(notification);
        var audit = await db.AuditEntries.SingleAsync(e => e.EntityId == dto.Id && e.Action == AuditAction.Created);
        Assert.Equal(nameof(ClassCourseTeacher), audit.EntityType);
        Assert.Equal(s.W.Tree.School1.Path, audit.OrgUnitPath);
    }

    [Fact]
    public async Task Assigning_ineligible_teacher_is_400_and_teacher_of_other_school_is_teacher_not_in_unit()
    {
        var s = await _t.SetUpAsync("TA2");
        var ineligible = await _t.TeacherAsync(s.W); // Teacher at School 1, no eligibility
        var elsewhere = await _t.TeacherAsync(s.W, s.W.Tree.School2);
        var plain = await PeopleTestKit.AddUserAsync(factory);

        var notEligible = await _t.AssignAsync(s, ineligible.User.Id);
        var otherSchool = await _t.AssignAsync(s, elsewhere.User.Id);
        var noRole = await _t.AssignAsync(s, plain.Id);
        var badRole = await _t.AssignAsync(s, ineligible.User.Id, "Boss");

        Assert.Equal(ProblemCodes.teacher_not_eligible, await CodeAsync(notEligible));
        Assert.Equal(ProblemCodes.teacher_not_in_unit, await CodeAsync(otherSchool));
        Assert.Equal(ProblemCodes.teacher_not_in_unit, await CodeAsync(noRole));
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
        Assert.Empty(await _t.TeachersAsync(s, history: true));
    }

    [Fact]
    public async Task Second_primary_is_409_and_replace_keeps_history()
    {
        var s = await _t.SetUpAsync("TA3");
        var first = await _t.EligibleTeacherAsync(s);
        var second = await _t.EligibleTeacherAsync(s);
        var firstRow = (await (await _t.AssignAsync(s, first.User.Id, from: Today.AddDays(-10))).Content.ReadFromJsonAsync<ClassCourseTeacherDto>())!;

        var conflict = await _t.AssignAsync(s, second.User.Id);
        var assistant = await _t.AssignAsync(s, second.User.Id, TeacherRoles.Assistant);
        var effective = Today.AddDays(1);
        var replace = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post,
            Routes.ClassCourseTeacherReplace(s.ClassCourse.Id, firstRow.Id), new ReplaceTeacherRequest(second.User.Id, effective));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(ProblemCodes.primary_teacher_exists, await CodeAsync(conflict));
        Assert.Equal(HttpStatusCode.Created, assistant.StatusCode);
        Assert.True(replace.StatusCode == HttpStatusCode.OK, await replace.Content.ReadAsStringAsync());
        var history = await _t.TeachersAsync(s, history: true);
        var old = history.Single(h => h.Id == firstRow.Id);
        Assert.Equal(effective.AddDays(-1), old.To);
        var next = history.Single(h => h.UserId == second.User.Id && h.Role == TeacherRoles.Primary);
        Assert.Equal(effective, next.From);
        Assert.Null(next.To);
        Assert.Equal(3, history.Count); // old primary, new primary, assistant
        Assert.Equal(2, (await _t.TeachersAsync(s)).Count(t => t.IsCurrent)); // old still current today + assistant; new starts tomorrow
    }

    [Fact]
    public async Task Replaced_teacher_loses_access_from_effective_date()
    {
        var s = await _t.SetUpAsync("TA4");
        var first = await _t.EligibleTeacherAsync(s);
        var second = await _t.EligibleTeacherAsync(s);
        var firstRow = (await (await _t.AssignAsync(s, first.User.Id, from: Today.AddDays(-10))).Content.ReadFromJsonAsync<ClassCourseTeacherDto>())!;

        var replace = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCourseTeacherReplace(s.ClassCourse.Id, firstRow.Id),
            new ReplaceTeacherRequest(second.User.Id, Today)); // effective today: the old teacher ended yesterday
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);

        async Task<bool> ReachesAsync(TeachingTestHost.Setup setup, Lms.Domain.Access.RoleAssignment who)
        {
            var ctx = new TestCurrentContext
            {
                UserId = who.UserId, OrganizationId = setup.W.Tree.Org.Id, AssignmentId = who.Id, ScopePath = who.OrgUnitPath,
            };
            await using var db = factory.CreateDbContext(ctx);
            var filter = await new RelationshipScope(db, ctx, new SystemClock()).ForAsync(Permissions.Classes.View, default);
            return filter.AllowsClassCourse(setup.ClassCourse.Id);
        }

        Assert.False(await ReachesAsync(s, first.Assignment));
        Assert.True(await ReachesAsync(s, second.Assignment));
    }

    [Fact]
    public async Task End_closes_the_assignment_and_rejects_bad_dates()
    {
        var s = await _t.SetUpAsync("TA5");
        var teacher = await _t.EligibleTeacherAsync(s);
        var row = (await (await _t.AssignAsync(s, teacher.User.Id, from: Today.AddDays(-5))).Content.ReadFromJsonAsync<ClassCourseTeacherDto>())!;

        var before = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCourseTeacherEnd(s.ClassCourse.Id, row.Id),
            new EndTeacherRequest(Today.AddDays(-6)));
        var ok = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCourseTeacherEnd(s.ClassCourse.Id, row.Id),
            new EndTeacherRequest(Today.AddDays(-1)));
        var again = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCourseTeacherEnd(s.ClassCourse.Id, row.Id),
            new EndTeacherRequest(Today));

        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(Today.AddDays(-1), (await ok.Content.ReadFromJsonAsync<ClassCourseTeacherDto>())!.To);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Empty(await _t.TeachersAsync(s));
        Assert.Single(await _t.TeachersAsync(s, history: true));
        // The primary slot is free again.
        var other = await _t.EligibleTeacherAsync(s);
        Assert.Equal(HttpStatusCode.Created, (await _t.AssignAsync(s, other.User.Id)).StatusCode);
    }

    [Fact]
    public async Task Supervisor_assigns_only_on_supervised_course_else_404()
    {
        var s = await _t.SetUpAsync("TA6");
        var teacher = await _t.TeacherAsync(s.W);
        // Courses owned at School 1, where the supervisor works (supervision follows the course owner unit, D37).
        var supervised = await AcademicTestKit.AddCourseAsync(factory, s.W.Tree.School1);
        var other = await AcademicTestKit.AddCourseAsync(factory, s.W.Tree.School1);
        var classCourses = new Dictionary<Guid, Guid>();
        foreach (var course in new[] { supervised, other })
        {
            await AcademicTestKit.AllocateAsync(factory, course, s.W.Tree.School1);
            var added = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(s.Class.Id),
                new AddClassCoursesRequest([course.Id], false));
            classCourses[course.Id] = (await added.Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0].Id;
        }

        var grant = await _t.GrantAsync(s.W, s.W.Tree.School1.Id, [teacher.User.Id], [supervised.Id, other.Id]);
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
        var supervisorUser = await PeopleTestKit.AddUserAsync(factory);
        var supervisor = await AccessTestData.AssignAsync(factory, s.W.Tree, s.W.Tree.School1, BuiltInRoles.Supervisor, supervisorUser.Id);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseSupervisor(await db.Set<Course>().SingleAsync(c => c.Id == supervised.Id), supervisorUser.Id));
            await db.SaveChangesAsync();
        }

        var mine = await _t.Classes.SendAsync(supervisor, HttpMethod.Post, Routes.ClassCourseTeachers(classCourses[supervised.Id]),
            new AssignTeacherRequest(teacher.User.Id, TeacherRoles.Primary, null));
        var notMine = await _t.Classes.SendAsync(supervisor, HttpMethod.Post, Routes.ClassCourseTeachers(classCourses[other.Id]),
            new AssignTeacherRequest(teacher.User.Id, TeacherRoles.Primary, null));

        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);
    }

    [Fact]
    public async Task Closed_year_or_class_blocks_assignment_409()
    {
        var s = await _t.SetUpAsync("TA7");
        var teacher = await _t.EligibleTeacherAsync(s);
        await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassClose(s.Class.Id));

        var response = await _t.AssignAsync(s, teacher.User.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(ProblemCodes.class_closed, await CodeAsync(response));
    }

    [Fact]
    public async Task From_outside_the_class_year_is_400()
    {
        var s = await _t.SetUpAsync("TA8");
        var teacher = await _t.EligibleTeacherAsync(s);

        var response = await _t.AssignAsync(s, teacher.User.Id, from: Today.AddYears(-3));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_assign_is_all_or_nothing()
    {
        var s = await _t.SetUpAsync("TA9");
        var teacher = await _t.EligibleTeacherAsync(s);
        var secondCourse = await AcademicTestKit.AddCourseAsync(factory, s.W.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, secondCourse, s.W.Tree.Root);
        var added = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(s.Class.Id),
            new AddClassCoursesRequest([secondCourse.Id], false));
        var second = (await added.Content.ReadFromJsonAsync<List<ClassCourseDto>>())![0]; // teacher is NOT eligible for this course

        var failed = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.TeacherAssignmentsBulk,
            new BulkAssignTeacherRequest(teacher.User.Id, TeacherRoles.Assistant, [s.ClassCourse.Id, second.Id]));
        await _t.GrantAsync(s.W, s.W.Tree.School1.Id, [teacher.User.Id], [secondCourse.Id]);
        var ok = await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.TeacherAssignmentsBulk,
            new BulkAssignTeacherRequest(teacher.User.Id, TeacherRoles.Assistant, [s.ClassCourse.Id, second.Id]));

        Assert.Equal(ProblemCodes.teacher_not_eligible, await CodeAsync(failed));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(2, (await ok.Content.ReadFromJsonAsync<List<ClassCourseTeacherDto>>())!.Count);
        Assert.Single(await _t.TeachersAsync(s, history: true));
    }

    [Fact]
    public async Task Teacher_sees_own_class_courses_only_and_student_sees_enrollments()
    {
        var s = await _t.SetUpAsync("TA10");
        var teacher = await _t.EligibleTeacherAsync(s);
        await _t.AssignAsync(s, teacher.User.Id);
        var other = await AcademicTestKit.AddCourseAsync(factory, s.W.Tree.Root);
        await AcademicTestKit.AllocateAsync(factory, other, s.W.Tree.Root);
        await _t.Classes.SendAsync(s.W.Admin, HttpMethod.Post, Routes.ClassCoursesOfClass(s.Class.Id), new AddClassCoursesRequest([other.Id], false));

        var mine = await _t.Classes.SendAsync(teacher.Assignment, HttpMethod.Get, Routes.MyClassCourses);
        var wrongYear = await _t.Classes.SendAsync(teacher.Assignment, HttpMethod.Get, Routes.MyClassCourses + $"?yearId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        var row = Assert.Single((await mine.Content.ReadFromJsonAsync<List<MyClassCourseDto>>())!);
        Assert.Equal(s.ClassCourse.Id, row.ClassCourseId);
        Assert.Equal(TeacherRoles.Primary, row.TeacherRole);
        Assert.Empty((await wrongYear.Content.ReadFromJsonAsync<List<MyClassCourseDto>>())!);

        var studentUser = await PeopleTestKit.AddUserAsync(factory);
        var profile = await PeopleTestKit.AddStudentAsync(factory, s.W.Tree.School1, PeopleTestKit.NewCode());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            (await db.Set<Lms.Domain.People.StudentProfile>().SingleAsync(p => p.Id == profile.Id)).LinkUser(studentUser.Id);
            var cls = await db.Set<Class>().SingleAsync(c => c.Id == s.Class.Id);
            var placement = new ClassStudent(cls, profile.Id, Today.AddDays(-2));
            db.Add(placement);
            await db.SaveChangesAsync();
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == s.ClassCourse.Id);
            db.Add(new Lms.Domain.Enrollment.CourseEnrollment(cc, placement, "Auto", Today.AddDays(-2)));
            await db.SaveChangesAsync();
        }

        var studentAssignment = await AccessTestData.AssignAsync(factory, s.W.Tree, s.W.Tree.School1, BuiltInRoles.Student, studentUser.Id);
        var studentRows = (await (await _t.Classes.SendAsync(studentAssignment, HttpMethod.Get, Routes.MyClassCourses)).Content.ReadFromJsonAsync<List<MyClassCourseDto>>())!;
        Assert.Equal(s.ClassCourse.Id, Assert.Single(studentRows).ClassCourseId);
        Assert.Null(studentRows[0].TeacherRole);
        Assert.Equal(1, studentRows[0].StudentCount);
    }

    [Fact]
    public async Task Other_clients_admin_gets_404_on_class_course_teachers()
    {
        var s = await _t.SetUpAsync("TA11");
        var teacher = await _t.EligibleTeacherAsync(s);
        var row = (await (await _t.AssignAsync(s, teacher.User.Id)).Content.ReadFromJsonAsync<ClassCourseTeacherDto>())!;
        var other = await _t.Classes.SeedAsync("TA11B", currentYear: true);

        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(other.Admin, HttpMethod.Get, Routes.ClassCourseTeachers(s.ClassCourse.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(other.Admin, HttpMethod.Post, Routes.ClassCourseTeachers(s.ClassCourse.Id),
            new AssignTeacherRequest(teacher.User.Id, TeacherRoles.Primary, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.Classes.SendAsync(other.Admin, HttpMethod.Post, Routes.ClassCourseTeacherEnd(s.ClassCourse.Id, row.Id),
            new EndTeacherRequest(Today))).StatusCode);
    }
}
