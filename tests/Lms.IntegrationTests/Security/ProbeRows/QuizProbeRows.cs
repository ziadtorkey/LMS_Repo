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
}
