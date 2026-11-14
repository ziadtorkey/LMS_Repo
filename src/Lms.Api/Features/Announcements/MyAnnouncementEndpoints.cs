using Lms.Api.Features.Forums;
using Lms.Api.Infrastructure;
using Lms.Domain.Identity;
using Lms.Infrastructure.Announcements;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Announcements;

/// <summary>The reader side (M6 Task 14): my announcements and their read state. Only the caller's own recipient rows are reachable; anything else is a 404.</summary>
internal static class MyAnnouncementEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(Routes.MyAnnouncements, ListAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.View);
        app.MapPost(Routes.MyAnnouncementReadPattern, ReadAsync).RequireAuthorization().RequirePermission(Permissions.Announcements.View);
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, MyAnnouncementsQuery query, LmsDbContext db, CancellationToken ct)
    {
        var (p, size, invalid) = ForumSupport.Paging(page, pageSize);
        if (invalid is not null)
        {
            return invalid;
        }

        var (items, total) = await query.PageAsync(p, size, ct);
        var ids = items.Select(i => i.Announcement.AuthorUserId).Distinct().ToList();
        var authors = (await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => (u.DisplayNameAr, u.DisplayNameEn));
        var dtos = items.Select(i =>
        {
            var a = i.Announcement;
            var (ar, en) = authors.GetValueOrDefault(a.AuthorUserId, (string.Empty, string.Empty));
            return new MyAnnouncementDto(
                a.Id, a.TitleAr, a.TitleEn, a.BodyAr, a.BodyEn, [], ar, en, DateTime.SpecifyKind(a.PublishedAtUtc ?? a.CreatedAt, DateTimeKind.Utc), a.IsPinned,
                i.IsRead, a.EditedAtUtc is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null, a.IsUrgent);
        }).ToList();
        return Results.Ok(new PagedResult<MyAnnouncementDto>(dtos, total, p, size));
    }

    private static async Task<IResult> ReadAsync(Guid id, MyAnnouncementsQuery query, CancellationToken ct) =>
        await query.MarkReadAsync(id, ct) ? Results.NoContent() : ApiProblems.NotFound();
}
