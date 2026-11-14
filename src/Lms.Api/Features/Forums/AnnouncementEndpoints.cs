using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Announcements;
using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Infrastructure.Announcements;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Forums;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Forums;

/// <summary>
/// The course board (FR-FOR-01), answering from the announcements store since M6 Task 17 (L15): a board entry is a published <c>Announcement</c> with one <c>ClassCourse</c>
/// target, the Staff and Students chips (Guardians off) and no fan-out of its own. Written by <c>Forums.Moderate</c> holders of the ClassCourse, read by everyone who sees its
/// forum. The contract is unchanged; ids are the ids the board always had (the migration keeps them). <c>Notify</c> sends Forums.Announcement to the enrolled students once, after
/// commit; an edit never notifies again. An announcement that reaches the course among other targets is listed read-only (<c>Source = School</c>) and is never edited here.
/// </summary>
public static class AnnouncementEndpoints
{
    private static readonly string[] BoardChips = [AnnouncementRoleFilters.Staff, AnnouncementRoleFilters.Students];

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.ClassCourseAnnouncementsPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Forums.View);
        app.MapPost(Routes.ClassCourseAnnouncementsPattern, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
        app.MapPut(Routes.AnnouncementPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
        app.MapDelete(Routes.AnnouncementPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Forums.Moderate);
    }

    private static bool Arabic(HttpContext http) =>
        http.Request.Headers.AcceptLanguage.ToString().TrimStart().StartsWith("ar", StringComparison.OrdinalIgnoreCase);

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

        var query = db.Set<Announcement>()
            .Where(a => a.Status == AnnouncementStates.Published
                        && db.Set<AnnouncementTarget>().Any(t => t.AnnouncementId == a.Id && t.Kind == AnnouncementTargetKinds.ClassCourse && t.TargetId == id))
            .OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.CreatedAt).ThenBy(a => a.Id);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return Results.Ok(new PagedResult<CourseAnnouncementDto>(await mapper.AnnouncementsAsync(rows, id, Arabic(http), ct), total, p, size));
    }

    private static async Task<IResult> CreateAsync(
        Guid id, CreateCourseAnnouncementRequest? request, HttpContext http, LmsDbContext db, ICurrentContext context,
        IRichTextSanitizer sanitizer, PlacementGuard guard, ForumMapper mapper, AnnouncementNotifier notifier, AnnouncementService announcements, CancellationToken ct)
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

        // The authoring unit is the unit of the class course, as the board row always was.
        var unit = await db.OrgUnits.FirstAsync(u => u.Id == classCourse!.OrgUnitId, ct);
        var announcement = new Announcement(unit, userId);
        var title = request.Title.Trim();
        announcement.SetContent(title, title, html, html, BoardChips, null, null, request.Pinned, false, false); // Notify stays false: the board notifies through its own notifier
        db.Set<Announcement>().Add(announcement);
        db.Set<AnnouncementTarget>().Add(new AnnouncementTarget(announcement, AnnouncementTargetKinds.ClassCourse, classCourse!.Id, false));
        await db.SaveChangesAsync(ct);
        await announcements.PublishNowAsync(announcement, ct); // the recipient snapshot for the reader feed
        if (request.Notify)
        {
            await notifier.NotifyAsync(announcement, title, classCourse, ct); // after commit, once
        }

        return Results.Created(Routes.Announcement(announcement.Id), (await mapper.AnnouncementsAsync([announcement], classCourse.Id, Arabic(http), ct))[0]);
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

        var title = request.Title.Trim();
        announcement!.EditPublished(html, html, announcement.ExpiresAtUtc, request.Pinned, DateTime.UtcNow); // body and pin, with the "edited" marker
        announcement.SetContent(
            title, title, html, html, announcement.RoleFilters, announcement.PublishAtUtc, announcement.ExpiresAtUtc, request.Pinned, announcement.Notify, announcement.IsUrgent);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await mapper.AnnouncementsAsync([announcement], classCourse!.Id, Arabic(http), ct))[0]);
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

        db.Set<Announcement>().Remove(announcement!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>A board entry by id: a published announcement whose only target is a class course the caller moderates. Anything else (another kind of announcement, several targets) is 404 here.</summary>
    private static async Task<(Announcement? Announcement, ClassCourse? ClassCourse, IResult? Problem)> LoadAsync(
        Guid id, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var announcement = await db.Set<Announcement>().FirstOrDefaultAsync(a => a.Id == id, ct);
        var targets = announcement is null ? [] : await db.Set<AnnouncementTarget>().Where(t => t.AnnouncementId == id).ToListAsync(ct);
        if (announcement is null || announcement.Status != AnnouncementStates.Published || targets.Count != 1
            || targets[0].Kind != AnnouncementTargetKinds.ClassCourse || targets[0].TargetId is not { } classCourseId)
        {
            return (null, null, ApiProblems.NotFound());
        }

        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Forums.Moderate, classCourseId, ct);
        return problem is null ? (announcement, classCourse, null) : (null, null, problem);
    }
}
