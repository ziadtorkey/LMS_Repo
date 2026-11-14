using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Grading;
using Lms.Infrastructure.Grading.Gradebooks;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Gradebook;

/// <summary>The entries of one student's row (<c>Grades.Manage</c>): manual scores, overrides with a reason, excuses, keep or drop an override (L12).</summary>
public static class GradeEntryEndpoints
{
    public static IEndpointRouteBuilder MapGradeEntryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut(Routes.ClassCourseGradebookEntriesPattern, UpsertAsync).RequirePermission(Permissions.Grades.Manage);
        return app;
    }

    private static async Task<IResult> UpsertAsync(
        Guid id, UpsertEntriesRequest? request, Guid? termId, HttpContext http, GradebookReader reader, GradebookEditor editor,
        ICurrentContext context, CancellationToken ct)
    {
        if (GradebookValidators.ValidateEntries(request) is { } invalid)
        {
            return invalid;
        }

        var (target, problem) = await GradebookAccess.ForWriteAsync(http, id, termId, reader, ct);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            await editor.ApplyEntriesAsync(target!.Gradebook, request!.StudentId, request.Version, request.Changes, context.UserId, ct);
        }
        catch (GradebookRuleException rule) when (rule.Code == ProblemCodes.concurrency_conflict)
        {
            // the answer is the current row, so the page can show what the other teacher changed
            return GradebookValidators.From(rule, await reader.RowAsync(target!.ClassCourse, target.Gradebook, request!.StudentId, ct));
        }
        catch (GradebookRuleException rule)
        {
            return GradebookValidators.From(rule);
        }
        catch (DbUpdateConcurrencyException)
        {
            return GradebookValidators.From(
                new GradebookRuleException(ProblemCodes.concurrency_conflict),
                await RereadAsync(reader, target!, request!.StudentId, ct));
        }

        return Results.Ok(await reader.RowAsync(target.ClassCourse, target.Gradebook, request!.StudentId, ct));
    }

    private static async Task<GradeRowDto?> RereadAsync(GradebookReader reader, GradebookTarget target, Guid studentId, CancellationToken ct) =>
        await reader.RowAsync(target.ClassCourse, target.Gradebook, studentId, ct);
}
