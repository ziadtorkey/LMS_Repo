using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Assignments;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Assignments.AssignmentTestKit;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Assignments;

/// <summary>M4a Task 8 (#358, FR-ASG-01): assignment authoring, module creation, validation and student visibility.</summary>
[Collection(ApiCollection.Name)]
public sealed class AssignmentTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    [Fact]
    public async Task Create_makes_the_assignment_and_its_module_and_the_student_sees_it()
    {
        var w = await _t.SetUpAsync("AS1");

        var created = await CreateAsync(_t, w.Teacher, w, Request("Essay", due: DateTime.UtcNow.AddDays(3)));

        Assert.Equal(ModuleKinds.Assignment, created.Module.Kind);
        Assert.Equal(CompletionRules.Submit, created.Module.CompletionRule);
        Assert.True(created.CanEdit);
        Assert.Equal(0, created.SubmittedCount);
        var modules = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1)))
            .Content.ReadFromJsonAsync<List<ModuleNodeDto>>();
        Assert.Contains(modules!, m => m.Id == created.Module.Id && m.Kind == ModuleKinds.Assignment);

        var asStudent = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignment(created.Id))).Content.ReadFromJsonAsync<CourseAssignmentDto>();
        Assert.False(asStudent!.CanEdit);
        Assert.Null(asStudent.SubmittedCount);
        var list = await (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.ClassCourseAssignments(w.ClassCourse.Id)))
            .Content.ReadFromJsonAsync<PagedResult<CourseAssignmentListItemDto>>();
        Assert.Equal([created.Id], list!.Items.Select(i => i.Id).ToArray());
        Assert.Null(list.Items[0].MyStatus);
    }

    [Fact]
    public async Task Penalty_policy_requires_percent()
    {
        var w = await _t.SetUpAsync("AS2");

        async Task AssertInvalidAsync(CreateCourseAssignmentRequest request)
        {
            var response = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.late_policy_invalid, await CodeAsync(response));
        }

        await AssertInvalidAsync(Request(latePolicy: LatePolicies.Penalty));
        await AssertInvalidAsync(Request(latePolicy: LatePolicies.Penalty, penalty: 0));
        await AssertInvalidAsync(Request(latePolicy: LatePolicies.Penalty, penalty: 101));
        await AssertInvalidAsync(Request(latePolicy: LatePolicies.Allow, penalty: 10));
        await AssertInvalidAsync(Request(latePolicy: LatePolicies.Block, due: null));
        var ok = await CreateAsync(_t, w.Teacher, w, Request(latePolicy: LatePolicies.Penalty, penalty: 20, due: DateTime.UtcNow.AddDays(1)));
        Assert.Equal(20, ok.PenaltyPercent);
    }

    [Fact]
    public async Task Other_input_rules_are_validated()
    {
        var w = await _t.SetUpAsync("AS3");
        foreach (var bad in new[]
                 {
                     Request(allowText: false, allowFiles: false), Request(maxGrade: 0), Request(maxGrade: 1001),
                     Request(resubmission: "Sometimes"), Request(title: " "),
                     Request(attachments: [Guid.NewGuid()]), Request(rule: CompletionRules.View) with { Module = Request().Module with { CompletionRule = CompletionRules.Manual } },
                 })
        {
            var response = await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), bad);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var unknownLesson = Request() with { LessonKey = Guid.NewGuid() };
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), unknownLesson)).StatusCode);
    }

    [Fact]
    public async Task Instructions_are_sanitised_on_write()
    {
        var w = await _t.SetUpAsync("AS4");

        var created = await CreateAsync(_t, w.Teacher, w, Request(instructions: "<p onclick=\"x()\">Hi<script>alert(1)</script><img src=\"a.png\"></p>"));

        Assert.DoesNotContain("script", created.InstructionsHtmlEn!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", created.InstructionsHtmlEn!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", created.InstructionsHtmlEn!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hi", created.InstructionsHtmlEn!);
        var tooLong = Request(instructions: new string('a', 200_001));
        Assert.Equal(HttpStatusCode.BadRequest, (await _t.SendAsync(w.Teacher, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), tooLong)).StatusCode);
    }

    [Fact]
    public async Task Group_targeted_assignment_hidden_from_other_group()
    {
        var w = await _t.SetUpAsync("AS5");
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var mine = await _t.AddGroupAsync(w.ClassCourse, w.StudentProfileId);
        await _t.AddGroupAsync(w.ClassCourse, other.ProfileId);

        var created = await CreateAsync(_t, w.Teacher, w, Request("Groupwork", target: ModuleTargets.Group, groupId: mine.Id));

        Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignment(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.CourseAssignment(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.CourseAssignmentMySubmission(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SubmitAsync(_t, other.Student, created.Id)).StatusCode);
        var list = await (await _t.SendAsync(other.Student, HttpMethod.Get, Routes.ClassCourseAssignments(w.ClassCourse.Id)))
            .Content.ReadFromJsonAsync<PagedResult<CourseAssignmentListItemDto>>();
        Assert.Empty(list!.Items);
    }

    [Fact]
    public async Task Hidden_or_scheduled_later_assignment_is_404_for_students_but_visible_to_staff()
    {
        var w = await _t.SetUpAsync("AS6");
        var hidden = await CreateAsync(_t, w.Teacher, w, Request("Hidden", visibility: ModuleVisibilities.Hidden));
        var later = Request("Later", visibility: ModuleVisibilities.Scheduled);
        later = later with { Module = later.Module with { VisibleFromUtc = DateTime.UtcNow.AddDays(2) } };
        var scheduled = await CreateAsync(_t, w.Teacher, w, later);

        foreach (var a in new[] { hidden, scheduled })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignment(a.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignment(a.Id))).StatusCode);
        }
    }

    [Fact]
    public async Task Update_changes_policy_and_module_and_delete_removes_both()
    {
        var w = await _t.SetUpAsync("AS7");
        var created = await CreateAsync(_t, w.Teacher, w, Request("Before"));
        var update = new UpdateCourseAssignmentRequest(
            created.Module is { } m ? new ModuleSettingsInput("بعد", "After", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Score, null) : null!,
            "<p>New</p>", null, [], DateTime.UtcNow.AddDays(5), 20, LatePolicies.Block, null, ResubmissionRules.UntilDue, true, false);

        var updated = await (await _t.SendAsync(w.Teacher, HttpMethod.Put, Routes.CourseAssignment(created.Id), update)).Content.ReadFromJsonAsync<CourseAssignmentDto>();

        Assert.Equal("After", updated!.Module.TitleEn);
        Assert.Equal(LatePolicies.Block, updated.LatePolicy);
        Assert.Equal(20, updated.MaxGrade);
        Assert.False(updated.AllowFiles);
        Assert.Equal(HttpStatusCode.NoContent, (await _t.SendAsync(w.Teacher, HttpMethod.Delete, Routes.CourseAssignment(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignment(created.Id))).StatusCode);
        var modules = await (await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, Lesson1)))
            .Content.ReadFromJsonAsync<List<ModuleNodeDto>>();
        Assert.DoesNotContain(modules!, x => x.Id == created.Module.Id);
    }

    [Fact]
    public async Task Writes_need_permission_and_the_relationship_to_the_class_course()
    {
        var w = await _t.SetUpAsync("AS8");
        var created = await CreateAsync(_t, w.Teacher, w, Request());

        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Post, Routes.ClassCourseAssignments(w.ClassCourse.Id), Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Get, Routes.CourseAssignment(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _t.SendAsync(w.OtherTeacher, HttpMethod.Delete, Routes.CourseAssignment(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Delete, Routes.CourseAssignment(created.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _t.SendAsync(w.Student, HttpMethod.Get, Routes.CourseAssignmentSubmissions(created.Id))).StatusCode);
    }

    [Fact]
    public async Task Rows_are_audited_and_carry_the_class_course_unit()
    {
        var w = await _t.SetUpAsync("AS9");
        var created = await CreateAsync(_t, w.Teacher, w, Request());

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var row = await db.Set<Assignment>().SingleAsync(a => a.Id == created.Id);
        Assert.Equal(w.ClassCourse.OrgUnitPath, row.OrgUnitPath);
        Assert.True(await db.AuditEntries.AnyAsync(e => e.EntityType == nameof(Assignment) && e.EntityId == created.Id));
    }
}
