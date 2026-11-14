using Lms.Api.Infrastructure;
using Lms.Domain.Quizzes;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Quizzes;

/// <summary>
/// Development only (the route does not exist elsewhere, 404): <c>POST /api/dev/jobs/quiz-close/{quizId}</c> runs
/// <see cref="QuizCloseJob"/> for one quiz now and waits for it, so the E2E demo does not wait for the sweep. The caller needs
/// Quizzes.Manage on the quiz class course. Not part of Lms.Shared and left out of the probe matrix on purpose: it is a dev tool.
/// </summary>
public static class DevQuizCloseTrigger
{
    public const string Pattern = "/api/dev/jobs/quiz-close/{quizId:guid}";

    public static void Map(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            return;
        }

        app.MapPost(Pattern, RunAsync).RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
    }

    private static async Task<IResult> RunAsync(Guid quizId, HttpContext http, LmsDbContext db, QuizCloseJob job, CancellationToken ct)
    {
        var quiz = await db.Set<Quiz>().FirstOrDefaultAsync(q => q.Id == quizId, ct);
        if (quiz is null)
        {
            return ApiProblems.NotFound();
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Quizzes.Manage, quiz.ClassCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var submitted = await job.RunForQuizAsync(quizId, ct);
        return Results.Ok(new { quizId, submitted });
    }
}

/// <summary>Registers the recurring <c>quiz-close</c> sweep at startup; one id is one schedule.</summary>
internal sealed class QuizCloseRegistration(IServiceScopeFactory scopes, ILogger<QuizCloseRegistration> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().AddOrUpdateRecurring<QuizCloseJob>(
                QuizCloseJob.RecurringJobId, job => job.RunAsync(CancellationToken.None), QuizCloseJob.Cron);
        }
        catch (Exception ex)
        {
            // Storage down at startup must not stop the API; the job is registered again on the next start.
            logger.LogError(ex, "The quiz-close recurring job could not be registered");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
