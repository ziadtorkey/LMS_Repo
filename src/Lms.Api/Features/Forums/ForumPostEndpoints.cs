using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Forums;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Forums;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Forums;

/// <summary>
/// Edit and delete a post. The author edits an own post in an unlocked thread (409 thread_locked otherwise); the author or a
/// moderator of the ClassCourse deletes one. Deleting the opening post deletes the thread.
/// </summary>
public static class ForumPostEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPut(Routes.ForumPostPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Post);
        app.MapDelete(Routes.ForumPostPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Post);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateForumPostRequest? request, HttpContext http, LmsDbContext db, ICurrentContext context, IClock clock,
        IRichTextSanitizer sanitizer, PlacementGuard guard, ForumMapper mapper, CancellationToken ct)
    {
        var (post, thread, problem) = await LoadAsync(id, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (post!.AuthorUserId != context.UserId)
        {
            return ForumSupport.Forbidden(http);
        }

        if (thread!.IsLocked)
        {
            return ApiProblems.Conflict(ProblemCodes.thread_locked);
        }

        if (request is null)
        {
            return ForumSupport.Invalid("body", "required");
        }

        if (ForumSupport.SanitizeBody(sanitizer, request.BodyHtml, out var html) is { } invalid)
        {
            return invalid;
        }

        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().FirstAsync(c => c.Id == post.ClassCourseId, ct);
        if (await ForumSupport.WritableAsync(classCourse, db, guard, ct) is { } closed)
        {
            return closed;
        }

        post.Edit(html, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        var moderator = await ForumSupport.HoldsAsync(http, Permissions.Forums.Moderate, post.ClassCourseId, ct);
        return Results.Ok((await mapper.PostsAsync([post], thread.IsLocked, moderator, ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, ICurrentContext context, PlacementGuard guard, CancellationToken ct)
    {
        var (post, thread, problem) = await LoadAsync(id, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (post!.AuthorUserId != context.UserId && !await ForumSupport.HoldsAsync(http, Permissions.Forums.Moderate, post.ClassCourseId, ct))
        {
            return ForumSupport.Forbidden(http);
        }

        if (post.IsOpeningPost)
        {
            return await ForumThreadEndpoints.RemoveThreadAsync(thread!, db, guard, ct);
        }

        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().FirstAsync(c => c.Id == post.ClassCourseId, ct);
        if (await ForumSupport.WritableAsync(classCourse, db, guard, ct) is { } closed)
        {
            return closed;
        }

        db.Set<ForumPostAttachment>().RemoveRange(await db.Set<ForumPostAttachment>().Where(a => a.PostId == id).ToListAsync(ct));
        db.Set<ForumPost>().Remove(post);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<(ForumPost? Post, ForumThread? Thread, IResult? Problem)> LoadAsync(
        Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var post = await db.Set<ForumPost>().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var (thread, problem) = await ForumThreadEndpoints.LoadAsync(post.ThreadId, Permissions.Forums.Post, http, db, ct);
        return problem is null ? (post, thread, null) : (null, null, problem);
    }
}
