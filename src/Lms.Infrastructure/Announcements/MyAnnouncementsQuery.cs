using Lms.Domain.Abstractions;
using Lms.Domain.Announcements;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Announcements;

/// <summary>An announcement as one recipient sees it: the row, and their own read state.</summary>
public sealed record MyAnnouncement(Announcement Announcement, bool IsRead);

/// <summary>
/// What a person reads (M6 Task 14): only announcements they hold a recipient row for, published and not expired, pinned first then newest. An announcement
/// is authored at a unit that usually lies above the reader's scope, so the unit filter would hide it; the read therefore starts from the reader's own
/// recipient rows (own user, client filter kept) and re-applies the rest of the filters by hand. Nobody else's announcement, draft or withdrawn row is reachable.
/// </summary>
public sealed class MyAnnouncementsQuery(LmsDbContext db, ICurrentContext context, IClock clock)
{
    /// <summary>The caller's own live announcements with their read state; empty without a user.</summary>
    private sealed class Row
    {
        public Announcement A { get; init; } = null!;
        public AnnouncementRecipient R { get; init; } = null!;
    }

    private IQueryable<Row> Mine()
    {
        var me = context.UserId;
        var now = clock.UtcNow;
        return
            // ISOLATION-BYPASS: the announcement sits at its authoring unit, above the reader's scope; reached only through the reader's own
            // recipient row (own user, client filter kept), same client, live, published and not expired.
            from r in db.Set<AnnouncementRecipient>()
            where r.UserId == me
            join a in db.Set<Announcement>().IgnoreQueryFilters() on r.AnnouncementId equals a.Id
            where !a.IsDeleted && a.OrganizationId == r.OrganizationId && a.Status == AnnouncementStates.Published
                  && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > now)
            select new Row { A = a, R = r };
    }

    public async Task<(IReadOnlyList<MyAnnouncement> Items, int Total)> PageAsync(int page, int size, CancellationToken ct)
    {
        var query = Mine();
        var rows = await query.OrderByDescending(x => x.A.IsPinned).ThenByDescending(x => x.A.PublishedAtUtc).ThenBy(x => x.A.Id)
            .Skip((page - 1) * size).Take(size).Select(x => new { x.A, Read = x.R.ReadAtUtc != null }).ToListAsync(ct);
        return ([.. rows.Select(x => new MyAnnouncement(x.A, x.Read))], await query.CountAsync(ct));
    }

    /// <summary>Marks the caller's own row read; false (404) when it is not theirs.</summary>
    public async Task<bool> MarkReadAsync(Guid announcementId, CancellationToken ct)
    {
        var me = context.UserId;
        var row = await db.Set<AnnouncementRecipient>().FirstOrDefaultAsync(r => r.AnnouncementId == announcementId && r.UserId == me, ct);
        if (row is null)
        {
            return false;
        }

        row.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// The comms announcements that reach one class course, once each: those the caller was reached by (or wrote) with a target of the class course, its class
    /// or one of its groups. The course page merges them with the course board (L15).
    /// </summary>
    public async Task<(IReadOnlyList<Announcement> Items, int Total)> ForClassCourseAsync(Guid classCourseId, Guid classId, int take, CancellationToken ct)
    {
        var me = context.UserId;
        var now = clock.UtcNow;
        var reached = db.Set<AnnouncementTarget>().Where(t =>
            (t.Kind == AnnouncementTargetKinds.ClassCourse && t.TargetId == classCourseId)
            || (t.Kind == AnnouncementTargetKinds.Class && t.TargetId == classId)
            || (t.Kind == AnnouncementTargetKinds.Group
                && db.Set<Lms.Domain.Enrollment.StudentGroup>().Any(g => g.Id == t.TargetId && g.ClassCourseId == classCourseId)))
            .Select(t => t.AnnouncementId);

        var viaRecipient = Mine().Where(x => reached.Contains(x.A.Id)).Select(x => x.A);
        var written = db.Set<Announcement>().Where(a => a.AuthorUserId == me && reached.Contains(a.Id)
                                                         && a.Status == AnnouncementStates.Published && (a.ExpiresAtUtc == null || a.ExpiresAtUtc > now));
        var all = viaRecipient.Union(written);
        var rows = await all.OrderByDescending(a => a.IsPinned).ThenByDescending(a => a.PublishedAtUtc).ThenBy(a => a.Id).Take(take).ToListAsync(ct);
        return (rows, await all.CountAsync(ct));
    }
}
