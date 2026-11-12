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
    }
}
