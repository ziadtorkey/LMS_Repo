using System.Net;
using Lms.Domain.Access;
using Lms.IntegrationTests.Access;
using Lms.IntegrationTests.Assignments;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.Library;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Library;
using Lms.Shared.Quizzes;
using static Lms.IntegrationTests.Quizzes.AttemptTestKit;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// The rest of M4a Task 29 (#379), after <see cref="M4aVisibilityProbeTests"/>: a library resource of one school seen from a sibling school and from another client, quiz
/// attempts that belong to one student, and a guardian on every teaching route (grades arrive in M5, D42). Each answer is one fixed code, so a change of order between the
/// permission check and the relationship check shows up: a missing permission is 403 before anything else, a relationship or visibility miss is 404.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class M4aLibraryQuizGuardianProbeTests(LmsApiFactory factory) : IDisposable
{
    private readonly ContentTestHost _t = new(factory);

    public void Dispose() => _t.Dispose();

    private static async Task ExpectAsync(Task<HttpResponseMessage> send, string what, HttpStatusCode expected)
    {
        using var response = await send;
        Assert.True(response.StatusCode == expected, $"{what}: expected {(int)expected}, got {(int)response.StatusCode}");
        if (expected == HttpStatusCode.NotFound)
        {
            Assert.DoesNotContain("secret-marker", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_library_resource_of_one_school_is_404_for_the_sibling_school_and_another_client()
    {
        using var w = await LibraryTestWorld.CreateAsync(factory, "LP1");
        var foreignTree = await AccessTestData.SeedAsync(factory, "LP1-other-client");
        var foreignAdmin = await AccessTestData.AssignAsync(factory, foreignTree, foreignTree.Root, BuiltInRoles.OrgAdmin, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var resource = await w.CreateAsync(w.School1Admin, LibraryTestWorld.Link(w.Tree.School1, "School 1 only secret-marker"));
        var edit = LibraryTestWorld.Link(w.Tree.School1, "hijacked");

        foreach (var (who, caller) in new (string, RoleAssignment)[] { ("admin of the sibling school", w.School2Admin), ("admin of another client", foreignAdmin) })
        {
            await ExpectAsync(w.SendAsync(caller, HttpMethod.Get, Routes.LibraryResource(resource.Id)), $"{who} GET", HttpStatusCode.NotFound);
            await ExpectAsync(w.SendAsync(caller, HttpMethod.Put, Routes.LibraryResource(resource.Id), edit), $"{who} PUT", HttpStatusCode.NotFound);
            await ExpectAsync(w.SendAsync(caller, HttpMethod.Delete, Routes.LibraryResource(resource.Id)), $"{who} DELETE", HttpStatusCode.NotFound);
        }

        // The owner's school reads it, the root admin reaches it by browsing the school (a library resource is visible below its owner, so a read by id from above is
        // not offered: 404 as well); a student may read but never change it.
        await ExpectAsync(w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.LibraryResource(resource.Id)), "owner GET", HttpStatusCode.OK);
        Assert.Contains((await w.ListAsync(w.Admin, "?orgUnitId=" + w.Tree.School1.Id)).Items, r => r.Id == resource.Id);
        await ExpectAsync(w.SendAsync(w.Student, HttpMethod.Put, Routes.LibraryResource(resource.Id), edit), "student PUT", HttpStatusCode.Forbidden);
        await ExpectAsync(w.SendAsync(w.Student, HttpMethod.Delete, Routes.LibraryResource(resource.Id)), "student DELETE", HttpStatusCode.Forbidden);

        // The resource survived all of it, unchanged, and the sibling school's list never shows it.
        var still = await w.ReadAsync<LibraryResourceDto>(await w.SendAsync(w.School1Admin, HttpMethod.Get, Routes.LibraryResource(resource.Id)));
        Assert.Equal("School 1 only secret-marker", still.TitleEn);
        Assert.DoesNotContain((await w.ListAsync(w.School2Admin)).Items, r => r.Id == resource.Id);
    }

    [Fact]
    public async Task A_quiz_attempt_belongs_to_its_student_and_the_staff_of_the_class_course()
    {
        var w = await _t.SetUpAsync("QP1");
        var classmate = await _t.EnrolledStudentAsync(w.Tree, w.Class, w.ClassCourse);
        var quiz = await _t.BuildAsync(w);
        var attempt = await _t.StartOkAsync(w.Student, quiz.Id);

        // Another student of the same class course holds every student permission and still gets a 404, not a 403 and not the payload.
        await ExpectAsync(_t.SendAsync(classmate.Student, HttpMethod.Get, Routes.QuizAttempt(attempt.Id)), "classmate GET attempt", HttpStatusCode.NotFound);
        await ExpectAsync(_t.AnswerAsync(classmate.Student, attempt.Id, attempt.Questions[0].QuestionId, []), "classmate answers", HttpStatusCode.NotFound);
        await ExpectAsync(_t.SendAsync(classmate.Student, HttpMethod.Post, Routes.QuizAttemptSubmit(attempt.Id)), "classmate submits", HttpStatusCode.NotFound);

        // A student who is not enrolled cannot start an attempt at all.
        await ExpectAsync(_t.StartAsync(w.Stranger, quiz.Id), "stranger starts an attempt", HttpStatusCode.NotFound);

        // Grading needs a grading permission: a student, even the owner, is refused with 403 before any relationship is looked at;
        // a teacher of another class course holds the permission and gets the 404.
        var grade = new GradeAttemptRequest([]);
        await ExpectAsync(_t.SendAsync(w.Student, HttpMethod.Put, Routes.QuizAttemptGrade(attempt.Id), grade), "owner grades own attempt", HttpStatusCode.Forbidden);
        await ExpectAsync(_t.SendAsync(classmate.Student, HttpMethod.Put, Routes.QuizAttemptGrade(attempt.Id), grade), "classmate grades", HttpStatusCode.Forbidden);
        await ExpectAsync(_t.SendAsync(w.OtherTeacher, HttpMethod.Put, Routes.QuizAttemptGrade(attempt.Id), grade), "teacher of another class course grades", HttpStatusCode.NotFound);

        // The owner's attempt is untouched and still theirs.
        await ExpectAsync(_t.SendAsync(w.Student, HttpMethod.Get, Routes.QuizAttempt(attempt.Id)), "owner GET attempt", HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_guardian_is_refused_on_every_teaching_route()
    {
        var w = await _t.SetUpAsync("GP1");
        var guardian = await AccessTestData.AssignAsync(factory, w.Tree, w.Tree.School1, BuiltInRoles.Guardian, (await PeopleTestKit.AddUserAsync(factory)).Id);
        var asset = await _t.UploadAssetAsync(w.Teacher, w.ClassCourse.Id);
        var module = await _t.CreateAsync(w.Teacher, w, ContentTestHost.AssetModule(ModuleKinds.File, asset.Id));
        var assignment = await AssignmentTestKit.CreateAsync(_t, w.Teacher, w, AssignmentTestKit.Request("Guardian probe"));
        var quiz = await _t.BuildAsync(w);

        foreach (var (route, url) in new (string, string)[]
                 {
                     ("module", Routes.Module(module.Node.Id)),
                     ("lesson modules", Routes.ClassCourseLessonModules(w.ClassCourse.Id, ContentTestHost.Lesson1)),
                     ("assignment", Routes.CourseAssignment(assignment.Id)),
                     ("quiz", Routes.Quiz(quiz.Id)),
                 })
        {
            await ExpectAsync(_t.SendAsync(guardian, HttpMethod.Get, url), $"guardian GET {route}", HttpStatusCode.Forbidden);
        }

        // The asset routes are guarded by Files.Download, which every role holds: the guardian reaches the relationship check and gets the 404 of a stranger.
        await ExpectAsync(_t.SendAsync(guardian, HttpMethod.Get, Routes.AssetUrl(asset.Id)), "guardian GET asset url", HttpStatusCode.NotFound);

        await ExpectAsync(_t.SendAsync(guardian, HttpMethod.Get, Routes.CourseAssignmentMySubmission(assignment.Id)), "guardian GET my submission", HttpStatusCode.Forbidden);
        await ExpectAsync(_t.StartAsync(guardian, quiz.Id), "guardian starts an attempt", HttpStatusCode.Forbidden);
    }
}
