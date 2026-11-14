using Lms.Domain.Abstractions;
using Lms.Domain.Announcements;
using Lms.Infrastructure.Announcements;
using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Announcements;

public sealed class AnnouncementMapper(LmsDbContext db, DeliverySummaryReader summaries, IClock clock)
{
    public async Task<IReadOnlyList<AnnouncementDto>> ToDtosAsync(IReadOnlyList<Announcement> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var targets = (await db.Set<AnnouncementTarget>().AsNoTracking().Where(t => ids.Contains(t.AnnouncementId)).OrderBy(t => t.Id).ToListAsync(ct))
            .ToLookup(t => t.AnnouncementId);
        var authors = await AuthorsAsync(rows, ct);
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Utc);
        List<AnnouncementDto> dtos = [];
        foreach (var r in rows)
        {
            dtos.Add(ToDto(r, targets[r.Id], authors, await summaries.ForAsync(r, ct), now));
        }

        return dtos;
    }

    public async Task<IReadOnlyList<AnnouncementListItemDto>> ToListItemsAsync(IReadOnlyList<Announcement> rows, CancellationToken ct)
    {
        var authors = await AuthorsAsync(rows, ct);
        return [.. rows.Select(r =>
        {
            var (ar, en) = authors.GetValueOrDefault(r.AuthorUserId, (string.Empty, string.Empty));
            return new AnnouncementListItemDto(
                r.Id, r.TitleAr, r.TitleEn, r.Status, r.IsPinned, Utc(r.PublishAtUtc), Utc(r.PublishedAtUtc), Utc(r.ExpiresAtUtc), r.AudienceCount, ar, en, r.IsUrgent);
        })];
    }

    private async Task<Dictionary<Guid, (string Ar, string En)>> AuthorsAsync(IReadOnlyList<Announcement> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.AuthorUserId).Distinct().ToList();
        return (await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToListAsync(ct))
            .ToDictionary(u => u.Id, u => (u.DisplayNameAr, u.DisplayNameEn));
    }

    private static AnnouncementDto ToDto(
        Announcement r, IEnumerable<AnnouncementTarget> targets, Dictionary<Guid, (string Ar, string En)> authors, AnnouncementDeliverySummaryDto? summary, DateTime now)
    {
        var (ar, en) = authors.GetValueOrDefault(r.AuthorUserId, (string.Empty, string.Empty));
        return new AnnouncementDto(
            r.Id, r.OrgUnitId, r.TitleAr, r.TitleEn, r.BodyAr, r.BodyEn, r.Status,
            [.. targets.Select(t => new AnnouncementTargetDto(t.Kind, t.TargetId, t.IncludeSubtree))], r.RoleFilters,
            Utc(r.PublishAtUtc), Utc(r.PublishedAtUtc), Utc(r.ExpiresAtUtc), r.IsPinned, r.Notify, [], r.AuthorUserId, ar, en,
            r.AudienceCount, Utc(r.EditedAtUtc), DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc), summary, now, r.IsUrgent);
    }

    private static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
