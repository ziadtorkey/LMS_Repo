using Lms.Domain.Assignments;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Infrastructure;
using Lms.IntegrationTests.People;
using Lms.Shared;
using Lms.Shared.Assignments;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Grading of submissions (M4a Task 9): everything is checked at the class unit (School1); School 2 and the other client get 404, a
/// Teacher without an assignment to the class course gets 404 (relationship rule).
/// </summary>
public sealed class AssignmentGradingProbeRows : IProbeRowModule
{
    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = 4 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("submission grade", HttpMethod.Put, Routes.SubmissionGradePattern,
            async (w, _) => new ProbeRequest(Routes.SubmissionGrade(await NewSubmissionAsync(w, graded: false)), new GradeSubmissionRequest(5, "<p>ok</p>", null)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("submission return", HttpMethod.Post, Routes.SubmissionReturnPattern,
            async (w, _) => new ProbeRequest(Routes.SubmissionReturn(await NewSubmissionAsync(w, graded: true))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
    ];

    /// <summary>A handed-in submission (optionally graded) of a student in a class course at School1.</summary>
    private static async Task<Guid> NewSubmissionAsync(M1ProbeWorld w, bool graded)
    {
        var factory = w.Callers.Factory;
        var school1 = w.Callers.A.School1;
        var year = await AcademicTestKit.AddYearAsync(factory, school1);
        var cls = await AcademicTestKit.AddClassAsync(factory, school1, year);
        var course = await AcademicTestKit.AddCourseAsync(factory, school1);
        var classCourse = await AcademicTestKit.AddClassCourseAsync(factory, cls, course);
        var student = await PeopleTestKit.AddStudentAsync(factory, school1, PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var assignment = new Assignment(classCourse);
        assignment.Apply(null, null, null, 10, "Allow", null, "None", true, true);
        var submission = new Submission(assignment, student.Id);
        submission.HandIn("<p>x</p>", false, DateTime.UtcNow);
        db.AddRange(assignment, submission);
        if (graded)
        {
            var feedback = new SubmissionFeedback(submission);
            feedback.Set(5, null, 5, null, null, DateTime.UtcNow);
            submission.MarkGraded();
            db.Add(feedback);
        }

        await db.SaveChangesAsync();
        return submission.Id;
    }
}
