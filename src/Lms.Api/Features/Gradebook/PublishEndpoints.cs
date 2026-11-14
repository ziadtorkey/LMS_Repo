using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Publishing;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;

namespace Lms.Api.Features.Gradebook;

/// <summary>
/// <c>POST …/gradebook/items/publish</c> (<c>Grades.Publish</c>; a teacher's own class courses, 404 otherwise): publish now, on a schedule or hide again.
/// 409 <c>scale_provisional</c> without the acknowledgement, 400 for a schedule that is not after the server clock.
/// </summary>
public static class PublishEndpoints
{
    public static IEndpointRouteBuilder MapPublishEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ClassCourseGradebookPublishPattern, PublishAsync).RequirePermission(Permissions.Grades.Publish);
        return app;
    }

    private static async Task<IResult> PublishAsync(
        Guid id, PublishItemsRequest? request, HttpContext http, GradePublisher publisher, ICurrentContext context, CancellationToken ct)
    {
        if (request is null)
        {
            return GradebookValidators.Invalid("Body", "required");
        }

        var (cc, problem) = await http.CheckClassCourseAsync(Permissions.Grades.Publish, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            var outcome = await publisher.PublishAsync(cc!, request!, context.UserId, ct);
            return Results.Ok(new PublishResultDto(outcome.ItemsChanged, outcome.AffectedStudents, outcome.NotificationsQueued));
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
    }
}
