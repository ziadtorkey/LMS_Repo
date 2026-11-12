using Lms.Api.Infrastructure;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Quizzes;

namespace Lms.Api.Features.Quizzes;

/// <summary>Quizzes: builder, attempts and grading (M4a Tasks 10 and 11: FR-QZ-01..03).</summary>
public sealed class QuizzesModule : ILmsModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<QuizMapper>();
        services.AddScoped<QuizBuilderHandlers>();
        services.AddScoped<AttemptService>();
        services.AddScoped<QuizResults>();
        services.AddScoped<AttemptHandlers>();
        services.AddScoped<QuizResultHandlers>();
        services.AddScoped<QuizCloseJob>();
        if (configuration.GetValue("Jobs:ServerEnabled", true))
        {
            services.AddHostedService<QuizCloseRegistration>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseQuizzesPattern,
                (Guid id, HttpContext http, QuizBuilderHandlers h, CancellationToken ct, int page = 1, int pageSize = Paging.DefaultPageSize) =>
                    h.ListAsync(id, page, pageSize, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.View);
        app.MapPost(Routes.ClassCourseQuizzesPattern,
                (Guid id, CreateQuizRequest? r, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.CreateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapGet(Routes.QuizPattern, (Guid id, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapPut(Routes.QuizPattern,
                (Guid id, UpdateQuizRequest? r, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.UpdateAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapPost(Routes.QuizQuestionsPattern,
                (Guid id, QuizQuestionInput? r, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.AddQuestionAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapPut(Routes.QuizQuestionsOrderPattern,
                (Guid id, ReorderQuizQuestionsRequest? r, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) =>
                    h.ReorderQuestionsAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapGet(Routes.QuizPreviewPattern, (Guid id, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.PreviewAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapPut(Routes.QuizQuestionPattern,
                (Guid id, QuizQuestionInput? r, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.UpdateQuestionAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);
        app.MapDelete(Routes.QuizQuestionPattern,
                (Guid id, HttpContext http, QuizBuilderHandlers h, CancellationToken ct) => h.DeleteQuestionAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Manage);

        // Attempts (M4a Task 11): the student routes need Quizzes.Attempt, the reads Quizzes.View, the hand grading Quizzes.Grade.
        app.MapGet(Routes.QuizSummaryPattern, (Guid id, HttpContext http, QuizResultHandlers h, CancellationToken ct) => h.SummaryAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.View);
        app.MapPost(Routes.QuizAttemptsPattern, (Guid id, HttpContext http, AttemptHandlers h, CancellationToken ct) => h.StartAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Attempt);
        app.MapGet(Routes.QuizAttemptsPattern,
                (Guid id, HttpContext http, QuizResultHandlers h, CancellationToken ct, int page = 1, int pageSize = Paging.DefaultPageSize) =>
                    h.ListAsync(id, page, pageSize, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.View);
        app.MapGet(Routes.QuizAttemptPattern, (Guid id, HttpContext http, AttemptHandlers h, CancellationToken ct) => h.GetAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Attempt);
        app.MapPut(Routes.QuizAttemptAnswersPattern,
                (Guid id, SaveAnswerRequest? r, HttpContext http, AttemptHandlers h, CancellationToken ct) => h.SaveAnswerAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Attempt);
        app.MapPost(Routes.QuizAttemptSubmitPattern, (Guid id, HttpContext http, AttemptHandlers h, CancellationToken ct) => h.SubmitAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Attempt);
        app.MapGet(Routes.QuizAttemptResultPattern, (Guid id, HttpContext http, QuizResultHandlers h, CancellationToken ct) => h.ResultAsync(id, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.View);
        app.MapPut(Routes.QuizAttemptGradePattern,
                (Guid id, GradeAttemptRequest? r, HttpContext http, QuizResultHandlers h, CancellationToken ct) => h.GradeAsync(id, r, http, ct))
            .RequireAuthorization().RequirePermission(Permissions.Quizzes.Grade);
        DevQuizCloseTrigger.Map(app);
    }
}
