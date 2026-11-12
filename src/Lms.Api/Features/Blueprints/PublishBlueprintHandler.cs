using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Blueprints;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Blueprints;

/// <summary>POST /api/courses/{id}/blueprints/draft/publish: the draft becomes the next numbered version.</summary>
public sealed class PublishBlueprintHandler(
    LmsDbContext db, IClock clock, ICurrentContext context, BlueprintReader reader, BlueprintDraftHandlers drafts,
    IEnumerable<IBlueprintPublished> published)
{
    public async Task<IResult> HandleAsync(Guid courseId, PublishBlueprintRequest request, HttpContext http, CancellationToken ct)
    {
        var (course, problem) = await http.CheckCourseAsync(Permissions.Blueprints.Publish, courseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var draft = await drafts.FindDraftAsync(courseId, ct);
        if (draft is null)
        {
            return BlueprintValidators.Problem(StatusCodes.Status404NotFound, "Not found", ProblemCodes.blueprint_no_draft, "The course has no draft.");
        }

        if (request?.ApplyMode is null || !BlueprintApplyModes.All.Contains(request.ApplyMode))
        {
            return BlueprintValidators.Invalid(nameof(PublishBlueprintRequest.ApplyMode), "apply_mode_invalid");
        }

        var (sections, lessons) = await reader.TreeAsync(draft, ct);
        if (sections.Count == 0 || sections.Any(s => lessons.All(l => l.SectionId != s.Id)))
        {
            return BlueprintValidators.Problem(
                StatusCodes.Status400BadRequest, "Bad request", ProblemCodes.blueprint_empty, "Every section needs at least one lesson.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var versions = await db.Set<Blueprint>().Where(b => b.CourseId == courseId && b.Status != Blueprint.DraftStatus).ToListAsync(ct);
            foreach (var previous in versions.Where(b => b.Status == Blueprint.PublishedStatus))
            {
                previous.Supersede();
            }

            draft.NextRevision(); // the concurrency token: of two parallel publishes only one commits
            draft.Publish(versions.Count == 0 ? 1 : versions.Max(b => b.Version) + 1, request.ApplyMode, context.UserId, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException update && DbErrors.IsConcurrentKeyConflict(update)))
        {
            return BlueprintValidators.Problem(
                StatusCodes.Status409Conflict, "Conflict", ProblemCodes.blueprint_stale, "The draft was published or changed by someone else.");
        }

        var queued = 0;
        foreach (var handler in published)
        {
            queued += await handler.OnPublishedAsync(draft, ct);
        }

        _ = course;
        return Results.Ok(new PublishBlueprintResultDto((await reader.SummariesAsync([draft], ct))[0], queued));
    }
}
