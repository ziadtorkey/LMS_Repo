using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
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
/// Course announcements (FR-FOR-01): written by <c>Forums.Moderate</c> holders of the ClassCourse, read by everyone who sees its
/// forum. <c>Notify</c> sends Forums.Announcement to the enrolled students once, after commit; an edit never notifies again.
/// </summary>
public static class AnnouncementEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseAnnouncementsPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Forums.View);
        app.MapPost(Routes.ClassCourseAnnouncementsPattern, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
        app.MapPut(Routes.AnnouncementPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
        app.MapDelete(Routes.AnnouncementPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
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

        var query = db.Set<CourseAnnouncement>().Where(a => a.ClassCourseId == id)
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.CreatedAt).ThenBy(a => a.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<CourseAnnouncementDto>(await mapper.AnnouncementsAsync(rows, ct), total, p, size));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, CreateCourseAnnouncementRequest? request, HttpContext http, LmsDbContext db, ICurrentContext context,
        IRichTextSanitizer sanitizer, PlacementGuard guard, ForumMapper mapper, AnnouncementNotifier notifier, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Forums.Moderate, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null || context.UserId is not { } userId)
        {
            return ForumSupport.Invalid("body", "required");
        }

        var invalid = ForumSupport.ValidateTitle(request.Title);
        var html = string.Empty;
        invalid ??= ForumSupport.SanitizeBody(sanitizer, request.BodyHtml, out html);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await ForumSupport.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        var announcement = new CourseAnnouncement(classCourse!, request.Title, html, request.Pinned, userId);
        db.Set<CourseAnnouncement>().Add(announcement);
        await db.SaveChangesAsync(ct);
        if (request.Notify)
        {
            await notifier.NotifyAsync(announcement, classCourse!, ct); // after commit, once
        }

        return Results.Created(Routes.Announcement(announcement.Id), (await mapper.AnnouncementsAsync([announcement], ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateCourseAnnouncementRequest? request, HttpContext http, LmsDbContext db, IRichTextSanitizer sanitizer,
        PlacementGuard guard, ForumMapper mapper, CancellationToken ct)
    {
        var (announcement, classCourse, problem) = await LoadAsync(id, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (request is null)
        {
            return ForumSupport.Invalid("body", "required");
        }

        var invalid = ForumSupport.ValidateTitle(request.Title);
        var html = string.Empty;
        invalid ??= ForumSupport.SanitizeBody(sanitizer, request.BodyHtml, out html);
        if (invalid is not null)
        {
            return invalid;
        }

        if (await ForumSupport.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        announcement!.Update(request.Title, html, request.Pinned);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await mapper.AnnouncementsAsync([announcement], ct))[0]);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, LmsDbContext db, PlacementGuard guard, CancellationToken ct)
    {
        var (announcement, classCourse, problem) = await LoadAsync(id, http, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await ForumSupport.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        db.Set<CourseAnnouncement>().Remove(announcement!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<(CourseAnnouncement? Announcement, ClassCourse? ClassCourse, IResult? Problem)> LoadAsync(
        Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var announcement = await db.Set<CourseAnnouncement>().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (announcement is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Forums.Moderate, announcement.ClassCourseId, ct);
        return problem is null ? (announcement, classCourse, null) : (null, null, problem);
    }
}
