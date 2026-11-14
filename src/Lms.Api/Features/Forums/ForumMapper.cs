using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Domain.Forums;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Lms.Shared.Forums;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Forums;

/// <summary>Entities to the forum DTOs: author names, reply counts and attachments in a fixed number of queries.</summary>
internal sealed class ForumMapper(LmsDbContext db, ICurrentContext context)
{
    public async Task<IReadOnlyList<ForumThreadDto>> ThreadsAsync(IReadOnlyList<ForumThread> threads, CancellationToken ct)
    {
        var ids = threads.Select(t => t.Id).ToList();
        var replies = ids.Count == 0
            ? []
            : await db.Set<ForumPost>().Where(p => ids.Contains(p.ThreadId) && !p.IsOpeningPost)
                .GroupBy(p => p.ThreadId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var names = await NamesAsync(threads.Select(t => t.AuthorUserId), ct);
        return threads.Select(t =>
        {
            var (ar, en) = names.GetValueOrDefault(t.AuthorUserId);
            return new ForumThreadDto(
                t.Id, t.ClassCourseId, t.Title, t.AuthorUserId, ar ?? string.Empty, en ?? string.Empty, t.IsPinned, t.IsLocked,
                replies.GetValueOrDefault(t.Id), t.CreatedAt, t.LastActivityAtUtc);
        }).ToList();
    }

    /// <summary>The posts of one thread. <c>CanEdit</c> = own post in an unlocked thread; <c>CanDelete</c> = own post or a moderator.</summary>
    public async Task<IReadOnlyList<ForumPostDto>> PostsAsync(
        IReadOnlyList<ForumPost> posts, bool threadLocked, bool canModerate, CancellationToken ct)
    {
        var postIds = posts.Select(p => p.Id).ToList();
        var links = postIds.Count == 0
            ? []
            : await db.Set<ForumPostAttachment>().Where(a => postIds.Contains(a.PostId)).Select(a => new { a.PostId, a.AssetId }).ToListAsync(ct);
        var assetIds = links.Select(l => l.AssetId).Distinct().ToList();
        var assets = assetIds.Count == 0
            ? []
            : await db.Set<Asset>().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        var names = await NamesAsync(posts.Select(p => p.AuthorUserId), ct);
        return posts.Select(p =>
        {
            var (ar, en) = names.GetValueOrDefault(p.AuthorUserId);
            var own = context.UserId == p.AuthorUserId;
            var attachments = links.Where(l => l.PostId == p.Id && assets.ContainsKey(l.AssetId)).Select(l => ToDto(assets[l.AssetId])).ToList();
            return new ForumPostDto(
                p.Id, p.ThreadId, p.AuthorUserId, ar ?? string.Empty, en ?? string.Empty, p.BodyHtml, attachments, p.IsOpeningPost,
                own && !threadLocked, own || canModerate, p.CreatedAt, p.EditedAtUtc);
        }).ToList();
    }

    public async Task<IReadOnlyList<CourseAnnouncementDto>> AnnouncementsAsync(IReadOnlyList<CourseAnnouncement> rows, CancellationToken ct)
    {
        var names = await NamesAsync(rows.Select(a => a.AuthorUserId), ct);
        return rows.Select(a =>
        {
            var (ar, en) = names.GetValueOrDefault(a.AuthorUserId);
            return new CourseAnnouncementDto(
                a.Id, a.ClassCourseId, a.Title, a.BodyHtml, a.IsPinned, a.AuthorUserId, ar ?? string.Empty, en ?? string.Empty,
                a.CreatedAt, a.UpdatedAt);
        }).ToList();
    }

    public static AssetDto ToDto(Asset a) => new(
        a.Id, a.ClassCourseId, a.Kind, a.FileName, a.ContentType, a.SizeBytes, a.DurationSeconds,
        AssetTypes.ForFileName(a.FileName)?.Previewable == true, a.CreatedAt);

    /// <summary>Staff and student profile names by user id; a user without a visible profile has empty names.</summary>
    private async Task<Dictionary<Guid, (string? Ar, string? En)>> NamesAsync(IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        var result = new Dictionary<Guid, (string?, string?)>();
        if (ids.Count == 0)
        {
            return result;
        }

        foreach (var s in await db.Set<StaffProfile>().Where(p => ids.Contains(p.UserId)).Select(p => new { p.UserId, p.NameAr, p.NameEn }).ToListAsync(ct))
        {
            result[s.UserId] = (s.NameAr, s.NameEn);
        }

        foreach (var s in await db.Set<StudentProfile>().Where(p => p.UserId != null && ids.Contains(p.UserId.Value))
                     .Select(p => new { UserId = p.UserId!.Value, p.NameAr, p.NameEn }).ToListAsync(ct))
        {
            result.TryAdd(s.UserId, (s.NameAr, s.NameEn));
        }

        return result;
    }
}
