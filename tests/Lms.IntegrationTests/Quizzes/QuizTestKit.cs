using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.IntegrationTests.Content;
using Lms.Shared;
using Lms.Shared.Content;
using Lms.Shared.Quizzes;
using static Lms.IntegrationTests.Content.ContentTestHost;

namespace Lms.IntegrationTests.Quizzes;

internal static class QuizTestKit
{
    public static QuizSettingsDto QuizSettings(
        int? timeLimit = null, int? maxAttempts = null, string method = GradingMethods.Highest, string review = ReviewPolicies.None,
        int? draw = null, DateTime? opens = null, DateTime? closes = null, bool shuffleQuestions = false, bool shuffleOptions = false) =>
        new(timeLimit, maxAttempts, method, shuffleQuestions, shuffleOptions, opens, closes, review, draw);

    public static ModuleSettingsInput ModuleSettings(string title = "Quiz", string visibility = ModuleVisibilities.Published, string rule = CompletionRules.Score) =>
        new("اختبار " + title, title, visibility, null, null, ModuleTargets.ClassCourse, null, rule, null);

    public static CreateQuizRequest CreateRequest(Guid lessonKey, QuizSettingsDto? settings = null, string title = "Quiz", string visibility = ModuleVisibilities.Published) =>
        new(lessonKey, ModuleSettings(title, visibility), "وصف", "About", settings ?? QuizSettings());

    public static QuizQuestionInput Single(string text = "Q", decimal points = 1, int correct = 0, int options = 3) =>
        new(QuestionTypes.SingleChoice, text, points, Enumerable.Range(0, options).Select(i => new QuizOptionInput("Option " + i, i == correct)).ToList());

    public static QuizQuestionInput Multi(string text = "M", decimal points = 2, params int[] correct) =>
        new(QuestionTypes.MultipleChoice, text, points, Enumerable.Range(0, 4).Select(i => new QuizOptionInput("Option " + i, correct.Contains(i))).ToList());

    public static QuizQuestionInput TrueFalse(string text = "T", decimal points = 1, bool answer = true) =>
        new(QuestionTypes.TrueFalse, text, points, [new QuizOptionInput("True", answer), new QuizOptionInput("False", !answer)]);

    public static QuizQuestionInput Essay(string text = "E", decimal points = 5) => new(QuestionTypes.Essay, text, points, []);

    public static async Task<QuizDto> CreateQuizAsync(
        this ContentTestHost host, RoleAssignment caller, ContentWorld w, QuizSettingsDto? settings = null, Guid? lesson = null,
        string title = "Quiz", string visibility = ModuleVisibilities.Published)
    {
        var response = await host.SendAsync(caller, HttpMethod.Post, Routes.ClassCourseQuizzes(w.ClassCourse.Id),
            CreateRequest(lesson ?? Lesson1, settings, title, visibility));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuizDto>())!;
    }

    public static async Task<QuizQuestionDto> AddQuestionAsync(this ContentTestHost host, RoleAssignment caller, Guid quizId, QuizQuestionInput input)
    {
        var response = await host.SendAsync(caller, HttpMethod.Post, Routes.QuizQuestionsOf(quizId), input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuizQuestionDto>())!;
    }

    public static async Task<QuizDto> GetQuizAsync(this ContentTestHost host, RoleAssignment caller, Guid quizId)
    {
        var response = await host.SendAsync(caller, HttpMethod.Get, Routes.Quiz(quizId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<QuizDto>())!;
    }
}
