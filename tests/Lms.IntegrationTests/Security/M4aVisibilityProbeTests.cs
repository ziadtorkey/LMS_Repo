using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using static Lms.IntegrationTests.Assignments.AssignmentTestKit;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// M4a visibility probes (plan Task 29, PRD §3.3 step 2, D42, D51): what a caller who <b>holds the permission</b> may still not reach, asked of one world through every
/// route family at once, so a new module, asset or assignment route that forgets its relationship check shows up here as one failing row per caller.
/// <see cref="M4aEndpointMatrixTests"/> covers the callers whose role lacks the permission (403). Here the callers hold it and the answer is decided by the data:
/// the teacher of another class course, a student of the same class course in <b>another group</b>, a student who is not enrolled, an admin of a sibling school and an
/// admin of another client must all get a <b>404</b> (never 403, never an empty 200) for a module, its asset, an assignment and a submission that are not theirs; the
/// owners and the staff of the class course get the payload. A miss is identical to a non-existent id.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4aVisibilityProbeTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private sealed record Callers(
        ContentWorld World, RoleAssignment OtherGroupStudent, RoleAssignment SiblingSchoolAdmin, RoleAssignment OtherClientAdmin, Lms.Domain.Enrollment.StudentGroup MyGroup);

    private async Task<Callers> WorldAsync(string name)
    {
        var w = await _t.SetUpAsync(name);
        var other = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var mine = await _t.AddGroupAsync(w.ClassCourse, w.StudentProfileId);
        await _t.AddGroupAsync(w.ClassCourse, other.ProfileId);
        var sibling = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School2, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var foreignTree = await AccessTestData.SeedAsync(factory, name + "-other-client");
        var foreign = await AccessTestData.AssignAsync(factory, foreignTree, foreignTree.Root, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);
        return new Callers(w, other.Student, sibling, foreign, mine);
    }

    private static string Describe(string route, string caller, HttpStatusCode got, HttpStatusCode expected) => $"{caller} on {route}: expected {(int)expected}, got {(int)got}";

    private async Task ExpectAsync(RoleAssignment caller, string who, string route, HttpMethod method, string url, HttpStatusCode expected, object? body = null)
    {
        using var response = await _t.SendAsync(caller, method, url, body);
        Assert.True(response.StatusCode == expected, Describe($"{method} {route}", who, response.StatusCode, expected));
        if (expected == HttpStatusCode.NotFound)
        {
            // A miss is a miss: the body never names the thing that exists.
            Assert.DoesNotContain("secret-marker", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_group_module_its_asset_and_its_lesson_listing_belong_to_that_group_only()
    {
        var c = await WorldAsync("VP1");
        var w = c.World;
        var asset = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var module = await _t.CreateAsync(
            w.Teacher, w, ContentTestHost.AssetModule(ModuleKinds.File, asset.Id, target: ModuleTargets.Group, groupId: c.MyGroup.Id));

        foreach (var (who, caller, expected) in new (string, RoleAssignment, HttpStatusCode)[]
                 {
                     ("teacher of the class course", w.Teacher, HttpStatusCode.OK),
                     ("student of the group", w.Student, HttpStatusCode.OK),
                     ("org admin", w.Admin, HttpStatusCode.OK),
                     ("student of the other group", c.OtherGroupStudent, HttpStatusCode.NotFound),
                     ("student not enrolled", w.Stranger, HttpStatusCode.NotFound),
                     ("teacher of another class course", w.OtherTeacher, HttpStatusCode.NotFound),
                     ("admin of the sibling school", c.SiblingSchoolAdmin, HttpStatusCode.NotFound),
                     ("admin of another client", c.OtherClientAdmin, HttpStatusCode.NotFound),
                 })
        {
            await ExpectAsync(caller, who, "module", HttpMethod.Get, Routes.Module(module.Node.Id), expected);
            await ExpectAsync(caller, who, "asset url", HttpMethod.Get, Routes.AssetUrl(asset.Id), expected);
        }

        // The lesson listing of a student of the other group never contains the module (and never errors).
        using var list = await _t.SendAsync(c.OtherGroupStudent, HttpMethod.Get, Routes.ClassCourseLessonModules(w.ClassCourse.Id, ContentTestHost.Lesson1));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(module.Node.Id, (await list.Content.ReadFromJsonAsync<List<ModuleNodeDto>>())!.Select(m => m.Id));
    }

    [Fact]
    public async Task A_hidden_module_and_its_asset_are_404_for_every_student_and_outsider_but_open_to_staff()
    {
        var c = await WorldAsync("VP2");
        var w = c.World;
        var asset = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var hidden = await _t.CreateAsync(w.Teacher, w, ContentTestHost.AssetModule(ModuleKinds.File, asset.Id, ModuleVisibilities.Hidden));

        foreach (var (who, caller, expected) in new (string, RoleAssignment, HttpStatusCode)[]
                 {
                     ("teacher of the class course", w.Teacher, HttpStatusCode.OK),
                     ("org admin", w.Admin, HttpStatusCode.OK),
                     ("student of the group", w.Student, HttpStatusCode.NotFound),
                     ("student of the other group", c.OtherGroupStudent, HttpStatusCode.NotFound),
                     ("student not enrolled", w.Stranger, HttpStatusCode.NotFound),
                     ("teacher of another class course", w.OtherTeacher, HttpStatusCode.NotFound),
                     ("admin of the sibling school", c.SiblingSchoolAdmin, HttpStatusCode.NotFound),
                     ("admin of another client", c.OtherClientAdmin, HttpStatusCode.NotFound),
                 })
        {
            await ExpectAsync(caller, who, "hidden module", HttpMethod.Get, Routes.Module(hidden.Node.Id), expected);
            await ExpectAsync(caller, who, "hidden module asset url", HttpMethod.Get, Routes.AssetUrl(asset.Id), expected);
        }
    }

    [Fact]
    public async Task A_group_assignment_and_its_submissions_belong_to_that_group_and_its_staff_only()
    {
        var c = await WorldAsync("VP3");
        var w = c.World;
        var assignment = await CreateAsync(_t, w.Teacher, w, Request("Group work", target: ModuleTargets.Group, groupId: c.MyGroup.Id));
        using (var submitted = await SubmitAsync(_t, w.Student, assignment.Id))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        foreach (var (who, caller, expected) in new (string, RoleAssignment, HttpStatusCode)[]
                 {
                     ("student of the other group", c.OtherGroupStudent, HttpStatusCode.NotFound),
                     ("student not enrolled", w.Stranger, HttpStatusCode.NotFound),
                     ("teacher of another class course", w.OtherTeacher, HttpStatusCode.NotFound),
                     ("admin of the sibling school", c.SiblingSchoolAdmin, HttpStatusCode.NotFound),
                     ("admin of another client", c.OtherClientAdmin, HttpStatusCode.NotFound),
                 })
        {
            await ExpectAsync(caller, who, "assignment", HttpMethod.Get, Routes.CourseAssignment(assignment.Id), expected);
            // the student routes need the submit permission: only a student gets as far as the relationship check (404), staff roles are refused with 403
            var studentRoute = who.StartsWith("student", StringComparison.Ordinal) ? expected : HttpStatusCode.Forbidden;
            await ExpectAsync(caller, who, "my submission", HttpMethod.Get, Routes.CourseAssignmentMySubmission(assignment.Id), studentRoute);
            // the staff list needs a grading permission: a student is refused with 403 before any relationship is looked at, everyone else who holds it gets the 404
            await ExpectAsync(caller, who, "submissions list", HttpMethod.Get, Routes.CourseAssignmentSubmissions(assignment.Id), who.StartsWith("student", StringComparison.Ordinal) ? HttpStatusCode.Forbidden : expected);
            using var submit = await SubmitAsync(_t, caller, assignment.Id);
            Assert.True(submit.StatusCode == studentRoute, Describe("POST submit", who, submit.StatusCode, studentRoute));
        }

        // The owner and the staff of the class course reach it; the submission list holds exactly the one submission.
        await ExpectAsync(w.Student, "student of the group", "assignment", HttpMethod.Get, Routes.CourseAssignment(assignment.Id), HttpStatusCode.OK);
        await ExpectAsync(w.Teacher, "teacher of the class course", "assignment", HttpMethod.Get, Routes.CourseAssignment(assignment.Id), HttpStatusCode.OK);
        using var list = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignmentSubmissions(assignment.Id));
        var page = (await list.Content.ReadFromJsonAsync<PagedResult<SubmissionListItemDto>>())!;
        var submissionId = Assert.Single(page.Items, i => i.SubmissionId is not null).SubmissionId!.Value;

        // A submission is readable by its owner and the class course's staff only (the grade route is the write side of the same check).
        foreach (var (who, caller, expected) in new (string, RoleAssignment, HttpStatusCode)[]
                 {
                     ("owner", w.Student, HttpStatusCode.OK),
                     ("teacher of the class course", w.Teacher, HttpStatusCode.OK),
                     ("student of the other group", c.OtherGroupStudent, HttpStatusCode.NotFound),
                     ("student not enrolled", w.Stranger, HttpStatusCode.NotFound),
                     ("teacher of another class course", w.OtherTeacher, HttpStatusCode.NotFound),
                     ("admin of the sibling school", c.SiblingSchoolAdmin, HttpStatusCode.NotFound),
                     ("admin of another client", c.OtherClientAdmin, HttpStatusCode.NotFound),
                 })
        {
            await ExpectAsync(caller, who, "submission", HttpMethod.Get, Routes.Submission(submissionId), expected);
        }

        using var foreignGrade = await _t.SendAsync(w.OtherTeacher, HttpMethod.Put, Routes.SubmissionGrade(submissionId), new GradeSubmissionRequest(5, "no", null));
        Assert.Equal(HttpStatusCode.NotFound, foreignGrade.StatusCode); // a teacher of another class course cannot grade it either
    }

    [Fact]
    public async Task A_student_cannot_read_or_change_another_students_attempt_or_submission_by_id()
    {
        var c = await WorldAsync("VP4");
        var w = c.World;
        var assignment = await CreateAsync(_t, w.Teacher, w, Request("Own work"));
        using (var submitted = await SubmitAsync(_t, w.Student, assignment.Id))
        {
            Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        }

        using var list = await _t.SendAsync(w.Teacher, HttpMethod.Get, Routes.CourseAssignmentSubmissions(assignment.Id));
        var submissionId = Assert.Single((await list.Content.ReadFromJsonAsync<PagedResult<SubmissionListItemDto>>())!.Items, i => i.SubmissionId is not null).SubmissionId!.Value;

        // A classmate holds the same permissions (same class course, other group) and still gets a 404, not a 403 and not the payload.
        await ExpectAsync(c.OtherGroupStudent, "classmate", "submission", HttpMethod.Get, Routes.Submission(submissionId), HttpStatusCode.NotFound);
        using var grade = await _t.SendAsync(c.OtherGroupStudent, HttpMethod.Put, Routes.SubmissionGrade(submissionId), new GradeSubmissionRequest(10, "mine", null));
        Assert.True(grade.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden, $"a student grading: got {(int)grade.StatusCode}");
        await ExpectAsync(c.OtherGroupStudent, "classmate", "my submission is their own, empty", HttpMethod.Get, Routes.CourseAssignmentMySubmission(assignment.Id), HttpStatusCode.OK);
    }
}
