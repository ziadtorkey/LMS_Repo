using System.Net;
using System.Net.Http.Json;
using Lms.Domain.Access;
using Lms.Domain.Quizzes;
using Lms.IntegrationTests.Content;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Quizzes;
using Microsoft.EntityFrameworkCore;
using static Lms.IntegrationTests.Quizzes.QuizTestKit;

namespace Lms.IntegrationTests.Quizzes;

/// <summary>A quiz as the builder returns it, with the questions the tests answer.</summary>
internal sealed record BuiltQuiz(QuizDto Quiz, IReadOnlyList<QuizQuestionDto> Questions)
{
    public Guid Id => Quiz.Id;

    public QuizQuestionDto Question(int index) => Questions[index];

    public IReadOnlyList<Guid> Correct(int index) => Questions[index].Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList();

    public IReadOnlyList<Guid> Wrong(int index) => [Questions[index].Options.First(o => !o.IsCorrect).Id];
}

internal static class AttemptTestKit
{
    /// <summary>A quiz with the given questions (one single choice, one multiple choice and one true / false by default).</summary>
    public static async Task<BuiltQuiz> BuildAsync(
        this ContentTestHost host, ContentWorld w, QuizSettingsDto? settings = null, params QuizQuestionInput[] questions)
    {
        var quiz = await host.CreateQuizAsync(w.Teacher, w, null);
        if (questions.Length == 0)
        {
            questions = [Single("S", 2, 0), Multi("M", 3, 0, 2), TrueFalse("T", 1, true)];
        }

        foreach (var input in questions)
        {
            await host.AddQuestionAsync(w.Teacher, quiz.Id, input);
        }

        var response = await host.SendAsync(w.Teacher, HttpMethod.Put, Routes.Quiz(quiz.Id),
            new UpdateQuizRequest(ModuleSettings(), null, null, settings ?? QuizSettings(review: ReviewPolicies.ScoreOnly)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var built = (await response.Content.ReadFromJsonAsync<QuizDto>())!;
        return new BuiltQuiz(built, built.Questions);
    }

    public static Task<HttpResponseMessage> StartAsync(this ContentTestHost host, RoleAssignment who, Guid quizId) =>
        host.SendAsync(who, HttpMethod.Post, Routes.QuizAttemptsOf(quizId));

    public static async Task<AttemptDto> StartOkAsync(this ContentTestHost host, RoleAssignment who, Guid quizId)
    {
        var response = await host.StartAsync(who, quizId);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AttemptDto>())!;
    }

    public static Task<HttpResponseMessage> AnswerAsync(
        this ContentTestHost host, RoleAssignment who, Guid attemptId, Guid questionId, IReadOnlyList<Guid> selected, long sequence = 1, string? text = null) =>
        host.SendAsync(who, HttpMethod.Put, Routes.QuizAttemptAnswers(attemptId), new SaveAnswerRequest(questionId, selected, text, sequence));

    public static async Task<AttemptResultDto> SubmitOkAsync(this ContentTestHost host, RoleAssignment who, Guid attemptId)
    {
        var response = await host.SendAsync(who, HttpMethod.Post, Routes.QuizAttemptSubmit(attemptId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AttemptResultDto>())!;
    }

    /// <summary>Starts, answers every question right (or all wrong), submits.</summary>
    public static async Task<AttemptResultDto> TakeAsync(this ContentTestHost host, RoleAssignment who, BuiltQuiz quiz, bool right = true)
    {
        var attempt = await host.StartOkAsync(who, quiz.Id);
        long sequence = 1;
        foreach (var asked in attempt.Questions)
        {
            var index = quiz.Questions.ToList().FindIndex(q => q.Id == asked.QuestionId);
            if (quiz.Questions[index].Type == QuestionTypes.Essay)
            {
                await host.AnswerAsync(who, attempt.Id, asked.QuestionId, [], sequence++, "essay text");
            }
            else
            {
                await host.AnswerAsync(who, attempt.Id, asked.QuestionId, right ? quiz.Correct(index) : quiz.Wrong(index), sequence++);
            }
        }

        return await host.SubmitOkAsync(who, attempt.Id);
    }

    public static async Task MutateAttemptAsync(LmsApiFactory factory, Guid attemptId, Action<QuizAttempt> change)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var attempt = await db.Set<QuizAttempt>().SingleAsync(a => a.Id == attemptId);
        change(attempt);
        await db.SaveChangesAsync();
    }

    /// <summary>Moves an attempt end into the past (EndsAtUtc is private-set: raw update).</summary>
    public static async Task SetEndAsync(LmsApiFactory factory, Guid attemptId, DateTime? endsAtUtc)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        await db.Set<QuizAttempt>().Where(a => a.Id == attemptId).ExecuteUpdateAsync(s => s.SetProperty(a => a.EndsAtUtc, endsAtUtc));
    }

    public static async Task SetQuizWindowAsync(LmsApiFactory factory, Guid quizId, DateTime? opens, DateTime? closes)
    {
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        await db.Set<Quiz>().Where(q => q.Id == quizId).ExecuteUpdateAsync(s => s.SetProperty(q => q.OpensAtUtc, opens).SetProperty(q => q.ClosesAtUtc, closes));
    }
}
