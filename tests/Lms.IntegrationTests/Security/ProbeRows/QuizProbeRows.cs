using Lms.Domain.Classes;
using Lms.IntegrationTests.Academic;
using Lms.IntegrationTests.Classes;
using Lms.Shared;
using Lms.Shared.Blueprints;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;
using static Lms.IntegrationTests.Security.Caller;
using static Lms.IntegrationTests.Security.ProbeExpect;

namespace Lms.IntegrationTests.Security;

/// <summary>
/// Quizzes (M4a Tasks 10 and 11): everything hangs off a class course at School1. School 2 and the other client get 404
/// (an invisible unit); a Teacher without an assignment to the class course gets 404 (relationship). The builder routes need
/// Quizzes.Manage, so a staff caller without it would get 403.
/// </summary>
public sealed class QuizProbeRows : IProbeRowModule
{
    private static readonly Guid Section = Guid.NewGuid(), Lesson1 = Guid.NewGuid();

    public IEnumerable<ProbeRow> Rows => Build().Select(r => r with { Milestone = 4 });

    private static IEnumerable<ProbeRow> Build() =>
    [
        new("class course quizzes list", HttpMethod.Get, Routes.ClassCourseQuizzesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseQuizzes((await NewClassCourseAsync(w)).Id)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz create", HttpMethod.Post, Routes.ClassCourseQuizzesPattern,
            async (w, _) => new ProbeRequest(Routes.ClassCourseQuizzes((await NewClassCourseAsync(w)).Id), CreateBody()),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("quiz builder get", HttpMethod.Get, Routes.QuizPattern,
            async (w, _) => new ProbeRequest(Routes.Quiz((await NewQuizAsync(w)).Quiz)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz update", HttpMethod.Put, Routes.QuizPattern,
            async (w, _) => new ProbeRequest(Routes.Quiz((await NewQuizAsync(w)).Quiz), UpdateBody()),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz preview", HttpMethod.Get, Routes.QuizPreviewPattern,
            async (w, _) => new ProbeRequest(Routes.QuizPreview((await NewQuizAsync(w)).Quiz)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz question add", HttpMethod.Post, Routes.QuizQuestionsPattern,
            async (w, _) => new ProbeRequest(Routes.QuizQuestionsOf((await NewQuizAsync(w)).Quiz), QuestionBody()),
            Expect(Created, Created, NotFound, NotFound, NotFound, Created)),
        new("quiz questions reorder", HttpMethod.Put, Routes.QuizQuestionsOrderPattern,
            async (w, _) =>
            {
                var (quiz, question) = await NewQuizAsync(w);
                return new ProbeRequest(Routes.QuizQuestionsOrder(quiz), new ReorderQuizQuestionsRequest([question]));
            },
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz question update", HttpMethod.Put, Routes.QuizQuestionPattern,
            async (w, _) => new ProbeRequest(Routes.QuizQuestion((await NewQuizAsync(w)).Question), QuestionBody()),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz question delete", HttpMethod.Delete, Routes.QuizQuestionPattern,
            async (w, _) => new ProbeRequest(Routes.QuizQuestion((await NewQuizAsync(w)).Question)),
            Expect(NoContent, NoContent, NotFound, NotFound, NotFound, NoContent)),
        // Attempts (Task 11). The student routes need Quizzes.Attempt, which no staff role holds (403); the platform admin holds no
        // student profile (404). The reads need Quizzes.View: staff reach them through the relationship, a student only their own.
        new("quiz summary", HttpMethod.Get, Routes.QuizSummaryPattern,
            async (w, _) => new ProbeRequest(Routes.QuizSummary((await NewQuizAsync(w)).Quiz)),
            Expect(Forbidden, Forbidden, NotFound, NotFound, NotFound, Forbidden)),
        new("quiz attempt start", HttpMethod.Post, Routes.QuizAttemptsPattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttemptsOf((await NewQuizAsync(w)).Quiz)),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("quiz attempts list", HttpMethod.Get, Routes.QuizAttemptsPattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttemptsOf((await NewQuizAsync(w)).Quiz)),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz attempt get", HttpMethod.Get, Routes.QuizAttemptPattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttempt(await NewAttemptAsync(w, graded: false))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("quiz attempt answer", HttpMethod.Put, Routes.QuizAttemptAnswersPattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttemptAnswers(await NewAttemptAsync(w, graded: false)),
                new SaveAnswerRequest(Guid.NewGuid(), [], null, 1)),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("quiz attempt submit", HttpMethod.Post, Routes.QuizAttemptSubmitPattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttemptSubmit(await NewAttemptAsync(w, graded: false))),
            Expect(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, NotFound)),
        new("quiz attempt result", HttpMethod.Get, Routes.QuizAttemptResultPattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttemptResult(await NewAttemptAsync(w, graded: true))),
            Expect(Ok, Ok, NotFound, NotFound, NotFound, Ok)),
        new("quiz attempt grade", HttpMethod.Put, Routes.QuizAttemptGradePattern,
            async (w, _) => new ProbeRequest(Routes.QuizAttemptGrade(await NewAttemptAsync(w, graded: true)),
                new GradeAttemptRequest([new EssayGradeInput(Guid.NewGuid(), 1, null)])),
            Expect(BadRequest, BadRequest, NotFound, NotFound, NotFound, BadRequest)), // no such essay: 400 once the attempt is reached
    ];

    private static ModuleSettingsInput Module() =>
        new("اختبار", "Quiz", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, CompletionRules.Score, null);

    private static QuizSettingsDto Settings() =>
        new(null, null, GradingMethods.Highest, false, false, null, null, ReviewPolicies.None, null);

    private static CreateQuizRequest CreateBody() => new(Lesson1, Module(), null, null, Settings());

    private static UpdateQuizRequest UpdateBody() => new(Module(), null, null, Settings());

    private static QuizQuestionInput QuestionBody() =>
        new(QuestionTypes.SingleChoice, "Q", 1, [new QuizOptionInput("A", true), new QuizOptionInput("B", false)]);

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

    /// <summary>A quiz with one question, created as the root admin.</summary>
    private static async Task<(Guid Quiz, Guid Question)> NewQuizAsync(M1ProbeWorld w)
    {
        var classCourse = await NewClassCourseAsync(w);
        var quiz = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.ClassCourseQuizzes(classCourse.Id), CreateBody());
        var quizId = quiz.GetProperty("id").GetGuid();
        var question = await w.Callers.SendOkAsync(RootAdmin, HttpMethod.Post, Routes.QuizQuestionsOf(quizId), QuestionBody());
        return (quizId, question.GetProperty("id").GetGuid());
    }

    /// <summary>An attempt of a fresh student at a fresh quiz (written straight to the database: staff cannot start attempts).</summary>
    private static async Task<Guid> NewAttemptAsync(M1ProbeWorld w, bool graded)
    {
        var (quizId, _) = await NewQuizAsync(w);
        var factory = w.Callers.Factory;
        var student = await Lms.IntegrationTests.People.PeopleTestKit.AddStudentAsync(factory, w.Callers.A.School1, Lms.IntegrationTests.People.PeopleTestKit.NewCode());
        await using var db = factory.CreateDbContext(Lms.IntegrationTests.Infrastructure.TestCurrentContext.PlatformAdmin());
        var quiz = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Set<Lms.Domain.Quizzes.Quiz>(), q => q.Id == quizId);
        var attempt = new Lms.Domain.Quizzes.QuizAttempt(quiz, student.Id, 1, DateTime.UtcNow, null, 1);
        if (graded)
        {
            attempt.Submit(Lms.Domain.Quizzes.QuizAttempt.Submitted, DateTime.UtcNow);
            attempt.SetResult(Lms.Domain.Quizzes.QuizAttempt.Graded, 1, false);
        }

        db.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }
}
