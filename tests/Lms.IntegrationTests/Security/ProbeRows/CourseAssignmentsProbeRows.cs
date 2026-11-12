using Lms.Domain.Classes;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.Shared;
using Lms.Shared.Assignments;
using Lms.Shared.Blueprints;
using Lms.Shared.Content;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Assignments of a class course and student submissions (M4a Task 8): everything hangs off a class course at School1. School 2
/// and the other client get 404 (an invisible unit); a Teacher without an assignment to the class course gets 404 (relationship).
/// Submit and my-submission belong to a student: staff lack the permission (403) and the platform admin finds no student (404).
/// </summary>
public sealed class CourseAssignmentsProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = 4 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("class course assignments list", HttpMethod.Get, Routes.ClassCourseAssignmentsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseAssignments((await NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("class course assignment create", HttpMethod.Post, Routes.ClassCourseAssignmentsPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseAssignments((await NewClassCourseAsync(w)).Id), CreateBody()),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("assignment get", HttpMethod.Get, Routes.CourseAssignmentPattern,
            async (w, _) => new ProbeRequest(Routes.CourseAssignment(await NewAssignmentAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("assignment update", HttpMethod.Put, Routes.CourseAssignmentPattern,
            async (w, _) => new ProbeRequest(Routes.CourseAssignment(await NewAssignmentAsync(w)), UpdateBody()),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("assignment delete", HttpMethod.Delete, Routes.CourseAssignmentPattern,
            async (w, _) => new ProbeRequest(Routes.CourseAssignment(await NewAssignmentAsync(w))),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        new("assignment submissions list", HttpMethod.Get, Routes.CourseAssignmentSubmissionsPattern,
            async (w, _) => new ProbeRequest(Routes.CourseAssignmentSubmissions(await NewAssignmentAsync(w))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("assignment my submission", HttpMethod.Get, Routes.CourseAssignmentMySubmissionPattern,
            async (w, _) => new ProbeRequest(Routes.CourseAssignmentMySubmission(await NewAssignmentAsync(w))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("assignment submit", HttpMethod.Post, Routes.CourseAssignmentSubmitPattern,
            async (w, _) => new ProbeRequest(Routes.CourseAssignmentSubmit(await NewAssignmentAsync(w)), new SubmitAssignmentRequest("<p>x</p>", [])),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("submission get", HttpMethod.Get, Routes.SubmissionPattern,
            (_, _) => Task.FromResult(new ProbeRequest(Routes.Submission(Guid.NewGuid()))),
            Expect(NotFound, NotFound, NotFound, NotFound, NotFound, NotFound)),
    ];

    private static CreateCourseAssignmentRequest CreateBody() =>
        new(Lesson1,
            new ModuleSettingsInput("مهمة", "Assignment", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Submit, null),
            "<p>Do it</p>", null, [], DateTime.UtcNow.AddDays(7), 10, LatePolicies.Allow, null, ResubmissionRules.None, true, true);

    private static UpdateCourseAssignmentRequest UpdateBody() =>
        new(CreateBody().Module, "<p>Changed</p>", null, [], DateTime.UtcNow.AddDays(8), 10, LatePolicies.Allow, null, ResubmissionRules.None, true, true);

    /// <summary>A class course with a one-lesson plan at School1.</summary>
    private static async Task<ClassCourse> NewClassCourseAsync(M1ProbeWorld w)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        await LessonPlanTestKit.PublishAsync(factory, course, BlueprintApplyModes.PushToExisting,
            new TestSection(Section, "Week 1", new TestLesson(Lesson1, "Intro")));
        return await LessonPlanTestKit.AddClassCourseWithPlanAsync(factory, cls, course);
    }

    private static async Task<Guid> NewAssignmentAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        var created = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseAssignments(classCourse.Id), CreateBody());
        return created.GetProperty("id").GetGuid();
    }
}
