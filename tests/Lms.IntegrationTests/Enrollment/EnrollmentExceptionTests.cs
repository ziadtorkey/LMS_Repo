using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Notifications;
using Lms.Domain.People;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.OrgUnits;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lms.IntegrationTests.Enrollment;

/// <summary>M3 Task 11 (#219), FR-CRS-03 and D40: exceptional enrollment request, approval, rejection and cancellation.</summary>
[Collection(ApiCollection.Name)]
public sealed class EnrollmentExceptionTests(LmsApiFactory factory) : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private readonly OrgUnitTestHost _host = new(factory);

    public void Dispose() => _host.Dispose();

    private sealed record Setup(EnrollmentWorld World, RoleAssignment Admin, RoleAssignment Counselor, Guid StudentId);

    /// <summary>Course A is for Muslims only; a Christian student is placed in the class (and so skips A, but takes B).</summary>
    private async Task<Setup> SetUpAsync(string name, string religion = "christian")
    {
        var w = await EnrollmentWorld.CreateAsync(factory, name);
        var admin = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.Root, BuiltInRoles.OrgAdmin, Guid.NewGuid());
        var counselor = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Counselor, Guid.NewGuid());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseRestriction(w.A, CourseRestrictionEvaluator.Religion, ["muslim"]));
            await db.SaveChangesAsync();
        }

        var student = await PeopleTestKit.AddStudentAsync(factory, w.Tree.School1, PeopleTestKit.NewCode());
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var tracked = await db.Set<StudentProfile>().SingleAsync(s => s.Id == student.Id);
            tracked.Update(tracked.NameAr, tracked.NameEn, null, null, tracked.DateOfBirth, tracked.Gender, religion, tracked.Nationality, null, null, null, null);
            await db.SaveChangesAsync();
        }

        var placed = await Send(admin, HttpMethod.Post, Routes.ClassStudents(w.Class.Id), new AddStudentsToClassRequest([student.Id], null, false));
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        return new Setup(w, admin, counselor, student.Id);
    }

    private Task<HttpResponseMessage> Send(RoleAssignment who, HttpMethod method, string url, object? body = null) =>
        _host.SendAsync(method, url, who, body);

    private async Task<EnrollmentExceptionDto> RequestOkAsync(Setup s, RoleAssignment? who = null, string reason = "Transferred from an Islamic school")
    {
        var response = await Send(who ?? s.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(s.StudentId, reason));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<EnrollmentExceptionDto>(Web))!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage r) =>
        (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    /// <summary>A supervisor of the course: a real user with a CourseSupervisor row and a Supervisor assignment at the school.</summary>
    private async Task<RoleAssignment> SupervisorOfAsync(Setup s, Course course)
    {
        var user = await PeopleTestKit.AddUserAsync(factory);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            db.Add(new CourseSupervisor(await db.Set<Course>().SingleAsync(c => c.Id == course.Id), user.Id));
            await db.SaveChangesAsync();
        }

        return await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School1, BuiltInRoles.Supervisor, user.Id);
    }

    private async Task<IReadOnlyList<Notification>> NotificationsAsync(Guid userId, string eventKey)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        return await db.Set<Notification>().Where(n => n.UserId == userId && n.EventKey == eventKey).ToListAsync();
    }

    [Fact]
    public async Task Counselor_requests_exception_for_christian_student_in_islamic_course_and_supervisor_is_notified()
    {
        var s = await SetUpAsync("EX1");
        var supervisor = await SupervisorOfAsync(s, s.World.A);
        var manager = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        var dto = await RequestOkAsync(s);

        Assert.Equal(("Pending", s.StudentId, s.World.CcA.Id), (dto.Status, dto.StudentId, dto.ClassCourseId));
        Assert.Equal(["Religion"], dto.FailedTypes.ToArray());
        Assert.Equal(s.World.A.NameEn, dto.CourseNameEn); // the course is owned at the root, above the class
        Assert.Single(await NotificationsAsync(supervisor.UserId, NotificationEvents.EnrollmentExceptionRequested));
        Assert.Empty(await NotificationsAsync(manager.UserId, NotificationEvents.EnrollmentExceptionRequested)); // supervised: not the manager
    }

    [Fact]
    public async Task Supervisor_approves_and_enrollment_is_Exceptional()
    {
        var s = await SetUpAsync("EX2");
        var supervisor = await SupervisorOfAsync(s, s.World.A);
        var dto = await RequestOkAsync(s);

        var response = await Send(supervisor, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest("ok"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var decided = (await response.Content.ReadFromJsonAsync<EnrollmentExceptionDto>(Web))!;
        Assert.Equal(("Approved", "ok"), (decided.Status, decided.DecisionComment));
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var enrollment = await db.Set<CourseEnrollment>().SingleAsync(e => e.ClassCourseId == s.World.CcA.Id && e.StudentId == s.StudentId);
        Assert.Equal((EnrollmentSources.Exceptional, CourseEnrollment.ActiveStatus), (enrollment.Source, enrollment.Status));
        Assert.Single(await NotificationsAsync(s.Counselor.UserId, NotificationEvents.EnrollmentExceptionDecided));

        var again = await Send(supervisor, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest(null));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.exception_closed), (again.StatusCode, await CodeAsync(again)));
    }

    [Fact]
    public async Task Supervisor_of_other_course_gets_404()
    {
        var s = await SetUpAsync("EX3");
        var ofA = await SupervisorOfAsync(s, s.World.A);
        var ofB = await SupervisorOfAsync(s, s.World.B);
        var dto = await RequestOkAsync(s);

        Assert.Equal(HttpStatusCode.NotFound, (await Send(ofB, HttpMethod.Get, Routes.EnrollmentException(dto.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ofB, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(ofB, HttpMethod.Post, Routes.EnrollmentExceptionReject(dto.Id), new DecideExceptionRequest("no"))).StatusCode);
        var listB = await (await Send(ofB, HttpMethod.Get, Routes.EnrollmentExceptions)).Content.ReadFromJsonAsync<PagedResult<EnrollmentExceptionDto>>(Web);
        var listA = await (await Send(ofA, HttpMethod.Get, Routes.EnrollmentExceptions)).Content.ReadFromJsonAsync<PagedResult<EnrollmentExceptionDto>>(Web);
        Assert.Empty(listB!.Items);
        Assert.Equal([dto.Id], listA!.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Course_without_supervisor_notifies_school_manager_who_decides_it()
    {
        var s = await SetUpAsync("EX4");
        var manager = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.Root, BuiltInRoles.Manager, Guid.NewGuid());

        var dto = await RequestOkAsync(s);

        Assert.Single(await NotificationsAsync(manager.UserId, NotificationEvents.EnrollmentExceptionRequested));
        var list = await (await Send(manager, HttpMethod.Get, Routes.EnrollmentExceptions)).Content.ReadFromJsonAsync<PagedResult<EnrollmentExceptionDto>>(Web);
        Assert.Equal([dto.Id], list!.Items.Select(i => i.Id));
        Assert.Equal(HttpStatusCode.OK, (await Send(manager, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest(null))).StatusCode);
    }

    [Fact]
    public async Task Managers_do_not_decide_the_requests_of_a_supervised_course()
    {
        var s = await SetUpAsync("EX5");
        await SupervisorOfAsync(s, s.World.A);
        var manager = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.Root, BuiltInRoles.Manager, Guid.NewGuid());
        var dto = await RequestOkAsync(s);

        Assert.Equal(HttpStatusCode.NotFound, (await Send(manager, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(s.Admin, HttpMethod.Get, Routes.EnrollmentException(dto.Id))).StatusCode);
        var list = await (await Send(manager, HttpMethod.Get, Routes.EnrollmentExceptions)).Content.ReadFromJsonAsync<PagedResult<EnrollmentExceptionDto>>(Web);
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Reject_requires_comment_and_frees_the_pair_for_a_new_request()
    {
        var s = await SetUpAsync("EX6");
        var dto = await RequestOkAsync(s);

        var blank = await Send(s.Admin, HttpMethod.Post, Routes.EnrollmentExceptionReject(dto.Id), new DecideExceptionRequest("  "));
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        var rejected = await Send(s.Admin, HttpMethod.Post, Routes.EnrollmentExceptionReject(dto.Id), new DecideExceptionRequest("not eligible"));

        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("Rejected", (await rejected.Content.ReadFromJsonAsync<EnrollmentExceptionDto>(Web))!.Status);
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            Assert.False(await db.Set<CourseEnrollment>().AnyAsync(e => e.ClassCourseId == s.World.CcA.Id && e.StudentId == s.StudentId));
        }

        Assert.Single(await NotificationsAsync(s.Counselor.UserId, NotificationEvents.EnrollmentExceptionDecided));
        await RequestOkAsync(s, reason: "second try");
    }

    [Fact]
    public async Task Second_pending_request_is_409_also_when_simultaneous()
    {
        var s = await SetUpAsync("EX7");
        await RequestOkAsync(s);

        var second = await Send(s.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(s.StudentId, "again"));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.exception_pending), (second.StatusCode, await CodeAsync(second)));

        var other = await SetUpAsync("EX7B");
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            Send(other.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(other.World.CcA.Id), new RequestExceptionRequest(other.StudentId, "race " + i))));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(3, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task Student_passing_restrictions_or_outside_the_class_cannot_request()
    {
        var muslim = await SetUpAsync("EX8", "muslim");
        var passes = await Send(muslim.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(muslim.World.CcA.Id), new RequestExceptionRequest(muslim.StudentId, "why"));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.validation_failed), (passes.StatusCode, await CodeAsync(passes)));

        var s = await SetUpAsync("EX8B");
        var stranger = await PeopleTestKit.AddStudentAsync(factory, s.World.Tree.School1, PeopleTestKit.NewCode());
        var outside = await Send(s.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(stranger.Id, "why"));
        Assert.Equal((HttpStatusCode.BadRequest, ProblemCodes.not_in_class), (outside.StatusCode, await CodeAsync(outside)));

        var noReason = await Send(s.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(s.StudentId, " "));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        var unknown = await Send(s.Counselor, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(Guid.NewGuid(), "why"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Requester_cancels_a_pending_request_and_nobody_else_can()
    {
        var s = await SetUpAsync("EX9");
        var dto = await RequestOkAsync(s);
        var otherCounselor = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School1, BuiltInRoles.Counselor, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await Send(otherCounselor, HttpMethod.Post, Routes.EnrollmentExceptionCancel(dto.Id))).StatusCode);
        var cancelled = await Send(s.Counselor, HttpMethod.Post, Routes.EnrollmentExceptionCancel(dto.Id));

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.Content.ReadFromJsonAsync<EnrollmentExceptionDto>(Web))!.Status);
        var again = await Send(s.Counselor, HttpMethod.Post, Routes.EnrollmentExceptionCancel(dto.Id));
        Assert.Equal((HttpStatusCode.Conflict, ProblemCodes.exception_closed), (again.StatusCode, await CodeAsync(again)));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(s.Admin, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest(null))).StatusCode);
    }

    [Fact]
    public async Task Teachers_cannot_request_or_decide_and_other_scopes_get_404()
    {
        var s = await SetUpAsync("EX10");
        var dto = await RequestOkAsync(s);
        var teacher = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School1, BuiltInRoles.Teacher, Guid.NewGuid());
        var school2 = await AccessTestData.AssignAsync(factory, s.World.Tree, s.World.Tree.School2, BuiltInRoles.Manager, Guid.NewGuid());
        var outsider = (await SetUpAsync("EX10B")).Admin;

        Assert.Equal(HttpStatusCode.Forbidden, (await Send(teacher, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(s.StudentId, "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(teacher, HttpMethod.Get, Routes.EnrollmentExceptions)).StatusCode);
        foreach (var who in new[] { school2, outsider })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await Send(who, HttpMethod.Get, Routes.EnrollmentException(dto.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(who, HttpMethod.Post, Routes.EnrollmentExceptionApprove(dto.Id), new DecideExceptionRequest(null))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Send(who, HttpMethod.Post, Routes.ClassCourseExceptions(s.World.CcA.Id), new RequestExceptionRequest(s.StudentId, "x"))).StatusCode);
        }
    }

    [Fact]
    public async Task Exception_rows_are_isolated()
    {
        var a = await AccessTestData.SeedAsync(factory, "EX11A");
        var b = await AccessTestData.SeedAsync(factory, "EX11B");
        var world = await EnrollmentWorld.CreateAsync(factory, "EX11C");
        var student = await PeopleTestKit.AddStudentAsync(factory, world.Tree.School2, PeopleTestKit.NewCode());
        Guid id;
        await using (var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            var cc = await db.Set<ClassCourse>().SingleAsync(c => c.Id == world.CcA.Id);
            var row = new EnrollmentException(cc, student.Id, "[\"Religion\"]", "why", Guid.NewGuid());
            db.Add(row);
            await db.SaveChangesAsync();
            id = row.Id;
        }

        // the class is at School1 (the row copies its unit); the other school and other clients never see it
        foreach (var (context, expected) in new[]
        {
            (TestCurrentContext.For(world.Tree.Org.Id, world.Tree.Root.Path), true),
            (TestCurrentContext.For(world.Tree.Org.Id, world.Tree.School1.Path), true),
            (TestCurrentContext.For(world.Tree.Org.Id, world.Tree.School2.Path), false),
            (TestCurrentContext.For(a.Org.Id, a.Root.Path), false),
            (TestCurrentContext.For(b.Org.Id, b.Root.Path), false),
            (TestCurrentContext.PlatformAdmin(), true),
        })
        {
            await using var db = factory.CreateDbContext(context);
            Assert.Equal(expected, await db.Set<EnrollmentException>().AnyAsync(e => e.Id == id));
        }
    }
}
