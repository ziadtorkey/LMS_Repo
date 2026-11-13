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
/// The forum of a ClassCourse (FR-FOR-01): threads, replies and moderation. Every route reaches a thread through its
/// ClassCourse, so the unit scope and the relationship rule (own class courses for a Teacher, own enrollments for a Student)
/// decide: a miss is 404, a missing permission 403.
/// </summary>
public static class ForumThreadEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseForumThreadsPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Forums.View);
        app.MapPost(Routes.ClassCourseForumThreadsPattern, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Post);
        app.MapGet(Routes.ForumThreadPattern, GetAsync).RequireAuthorization().RequirePermission(Permissions.Forums.View);
        app.MapDelete(Routes.ForumThreadPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
        app.MapPut(Routes.ForumThreadModerationPattern, ModerateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
        app.MapGet(Routes.ForumThreadPostsPattern, ListPostsAsync).RequireAuthorization().RequirePermission(Permissions.Forums.View);
        app.MapPost(Routes.ForumThreadPostsPattern, ReplyAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Post);
    }

    private static async Task<IResult> ListAsync(
        Guid id, int? page, int? pageSize, HttpContext http, LmsDbContext db, ForumMapper mapper, CancellationToken ct)
    {
        var (p, size, invalid) = ForumSupport.Paging(page, pageSize);
        if (invalid is not null)
        {
            return invalid;
        }

        var (_, problem) = await http.CheckClassCourseAsync(Permissions.Forums.View, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var query = db.Set<ForumThread>().Where(t => t.ClassCourseId == id)
            .OrderByDescending(t => t.IsPinned).ThenByDescending(t => t.LastActivityAtUtc).ThenBy(t => t.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<ForumThreadDto>(await mapper.ThreadsAsync(rows, ct), total, p, size));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, CreateForumThreadRequest? request, HttpContext http, LmsDbContext db, ICurrentContext context, IClock clock,
        IRichTextSanitizer sanitizer, PlacementGuard guard, ForumMapper mapper, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Forums.Post, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null || context.UserId is not { } userId)
        {
            return ForumSupport.Invalid("body", "required");
        }

        var invalid = ForumSupport.ValidateTitle(request.Title);
        string html = string.Empty;
        invalid ??= ForumSupport.SanitizeBody(sanitizer, request.BodyHtml, out html);
        invalid ??= await ForumSupport.ValidateAttachmentsAsync(request.AttachmentAssetIds, id, db, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await ForumSupport.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var thread = new ForumThread(classCourse!, request.Title, userId, clock.UtcNow);
        var post = new ForumPost(thread, userId, html, isOpeningPost: true);
        db.Set<ForumThread>().Add(thread);
        db.Set<ForumPost>().Add(post);
        db.Set<ForumPostAttachment>().AddRange((request.AttachmentAssetIds ?? []).Distinct().Select(a => new ForumPostAttachment(post, a)));
        await db.SaveChangesAsync(ct);
        var detail = await DetailAsync(thread, post, http, mapper, ct);
        return Results.Created(Routes.ForumThread(thread.Id), detail);
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, LmsDbContext db, ForumMapper mapper, CancellationToken ct)
    {
        var (thread, problem) = await LoadAsync(id, Permissions.Forums.View, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        var opening = await db.Set<ForumPost>().FirstOrDefaultAsync(p => p.ThreadId == id && p.IsOpeningPost, ct);
        return opening is null ? ApiProblems.NotFound() : Results.Ok(await DetailAsync(thread!, opening, http, mapper, ct));
    }

    private static async Task<IResult> ListPostsAsync(
        Guid id, int? page, int? pageSize, HttpContext http, LmsDbContext db, ForumMapper mapper, CancellationToken ct)
    {
        var (p, size, invalid) = ForumSupport.Paging(page, pageSize);
        if (invalid is not null)
        {
            return invalid;
        }

        var (thread, problem) = await LoadAsync(id, Permissions.Forums.View, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        var query = db.Set<ForumPost>().Where(x => x.ThreadId == id && !x.IsOpeningPost).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var moderator = await ForumSupport.HoldsAsync(http, Permissions.Forums.Moderate, thread!.ClassCourseId, ct);
        return Results.Ok(new PagedResult<ForumPostDto>(await mapper.PostsAsync(rows, thread.IsLocked, moderator, ct), total, p, size));
    }

    private static async Task<IResult> ReplyAsync(
        Guid id, CreateForumPostRequest? request, HttpContext http, LmsDbContext db, ICurrentContext context, IClock clock,
        IRichTextSanitizer sanitizer, PlacementGuard guard, ForumMapper mapper, CancellationToken ct)
    {
        var (thread, problem) = await LoadAsync(id, Permissions.Forums.Post, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null || context.UserId is not { } userId)
        {
            return ForumSupport.Invalid("body", "required");
        }

        if (thread!.IsLocked)
        {
            return ApiProblems.Conflict(ProblemCodes.thread_locked);
        }

        var invalid = ForumSupport.SanitizeBody(sanitizer, request.BodyHtml, out var html)
            ?? await ForumSupport.ValidateAttachmentsAsync(request.AttachmentAssetIds, thread.ClassCourseId, db, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().FirstAsync(c => c.Id == thread.ClassCourseId, ct);
        if (await ForumSupport.WritableAsync(classCourse, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var post = new ForumPost(thread, userId, html, isOpeningPost: false);
        db.Set<ForumPost>().Add(post);
        db.Set<ForumPostAttachment>().AddRange((request.AttachmentAssetIds ?? []).Distinct().Select(a => new ForumPostAttachment(post, a)));
        thread.Touch(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        var dto = (await mapper.PostsAsync([post], thread.IsLocked, await ForumSupport.HoldsAsync(http, Permissions.Forums.Moderate, thread.ClassCourseId, ct), ct))[0];
        return Results.Created(Routes.ForumPost(post.Id), dto);
    }

    private static async Task<IResult> ModerateAsync(
        Guid id, ModerateForumThreadRequest? request, HttpContext http, LmsDbContext db, PlacementGuard guard, ForumMapper mapper, CancellationToken ct)
    {
        var (thread, problem) = await LoadAsync(id, Permissions.Forums.Moderate, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null)
        {
            return ForumSupport.Invalid("body", "required");
        }

        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().FirstAsync(c => c.Id == thread!.ClassCourseId, ct);
        if (await ForumSupport.WritableAsync(classCourse, db, guard, ct) is { } closed)
        {
            return closed;
        }

        thread!.Moderate(request.IsPinned, request.IsLocked);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await mapper.ThreadsAsync([thread], ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, PlacementGuard guard, CancellationToken ct)
    {
        var (thread, problem) = await LoadAsync(id, Permissions.Forums.Moderate, http, db, ct);
        return problem ?? await RemoveThreadAsync(thread!, db, guard, ct);
    }

    /// <summary>Soft deletes the thread with its posts and attachment links (the caller has already been authorised).</summary>
    internal static async Task<IResult> RemoveThreadAsync(ForumThread thread, LmsDbContext db, PlacementGuard guard, CancellationToken ct)
    {
        var classCourse = await db.Set<Lms.Domain.Classes.ClassCourse>().FirstAsync(c => c.Id == thread.ClassCourseId, ct);
        if (await ForumSupport.WritableAsync(classCourse, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var posts = await db.Set<ForumPost>().Where(p => p.ThreadId == thread.Id).ToListAsync(ct);
        var postIds = posts.Select(p => p.Id).ToList();
        db.Set<ForumPostAttachment>().RemoveRange(await db.Set<ForumPostAttachment>().Where(a => postIds.Contains(a.PostId)).ToListAsync(ct));
        db.Set<ForumPost>().RemoveRange(posts);
        db.Set<ForumThread>().Remove(thread);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>The thread when the caller holds the permission on its ClassCourse; a miss or a hidden course is 404.</summary>
    internal static async Task<(ForumThread? Thread, IResult? Problem)> LoadAsync(
        Guid id, string permission, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var thread = await db.Set<ForumThread>().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (thread is null)
        {
            return (null, ApiProblems.NotFound());
        }

        var (_, problem) = await http.CheckClassCourseAsync(permission, thread.ClassCourseId, ct);
        return problem is null ? (thread, null) : (null, problem);
    }

    private static async Task<ForumThreadDetailDto> DetailAsync(
        ForumThread thread, ForumPost opening, HttpContext http, ForumMapper mapper, CancellationToken ct)
    {
        var moderator = await ForumSupport.HoldsAsync(http, Permissions.Forums.Moderate, thread.ClassCourseId, ct);
        var canPost = await ForumSupport.HoldsAsync(http, Permissions.Forums.Post, thread.ClassCourseId, ct);
        var dto = (await mapper.ThreadsAsync([thread], ct))[0];
        var post = (await mapper.PostsAsync([opening], thread.IsLocked, moderator, ct))[0];
        return new ForumThreadDetailDto(dto, post, canPost && !thread.IsLocked, moderator);
    }
}
