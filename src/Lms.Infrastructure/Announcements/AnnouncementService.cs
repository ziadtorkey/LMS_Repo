using Lms.Domain.Abstractions;
using Lms.Domain.Announcements;
using Lms.Infrastructure.Comms;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Announcements;

/// <summary>
/// Publishing, scheduling, withdrawing and expiring announcements (M6 Task 13). Publishing resolves the audience through the caller's context (so it stays
/// inside the author's scope) and freezes it into recipient rows; a scheduled announcement runs the same step later under the author's captured context, through
/// a keyed job that moves when the schedule is edited and is cancelled on withdraw. Delivery to channels is Task 14 (the fan-out hooks in after the snapshot).
/// </summary>
public sealed class AnnouncementService(
    LmsDbContext db, AudienceResolver resolver, IBackgroundJobs jobs, IClock clock, ISettingsResolver settings, CommsClientSettings comms)
{
    public static string PublishKey(Guid id) => $"announcement-publish:{id:N}";

    public static string ExpireKey(Guid id) => $"announcement-expire:{id:N}";

    /// <summary>The chips an author reaches when none is chosen: staff and students, and guardians too for a unit-level author (a teacher reaches no guardians by default).</summary>
    public static IReadOnlyList<string> EffectiveChips(IReadOnlyList<string>? chosen, bool unitLevelAuthor) =>
        chosen is { Count: > 0 }
            ? [.. chosen.Distinct()]
            : unitLevelAuthor ? [.. AnnouncementRoleFilters.All] : [AnnouncementRoleFilters.Staff, AnnouncementRoleFilters.Students];

    public async Task<Audience> AudienceAsync(Announcement announcement, CancellationToken ct)
    {
        var targets = await db.Set<AnnouncementTarget>().AsNoTracking().Where(t => t.AnnouncementId == announcement.Id)
            .Select(t => new AudienceTarget(t.Kind, t.TargetId, t.IncludeSubtree)).ToListAsync(ct);
        return await resolver.ResolveAsync(announcement.OrganizationId, targets, announcement.RoleFilters, announcement.AuthorUserId, DateOnly.FromDateTime(clock.UtcNow), ct);
    }

    /// <summary>The per-author daily cap (<c>Announcements.MaxPerAuthorPerDay</c>), counted in the client's day: false when the author is at it.</summary>
    public async Task<bool> UnderAuthorCapAsync(Announcement announcement, CancellationToken ct)
    {
        var cap = (await settings.ResolveAsync(SettingKeys.AnnouncementsMaxPerAuthorPerDay, announcement.OrgUnitPath, ct)).Value;
        var zone = (await comms.GetAsync(announcement.OrganizationId, ct)).Zone;
        var (dayStart, _) = Lms.Domain.Comms.DeliveryPolicy.ClientDay(clock.UtcNow, zone);
        var used = await db.Set<Announcement>().CountAsync(
            a => a.AuthorUserId == announcement.AuthorUserId && a.Id != announcement.Id && a.CreatedAt >= dayStart && a.Status != AnnouncementStates.Draft, ct);
        return used < cap;
    }

    /// <summary>Publishes now, or schedules when the publish time is still ahead. The caller has checked the cap.</summary>
    public async Task PublishAsync(Announcement announcement, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (announcement.PublishAtUtc is { } at && at > now)
        {
            announcement.Schedule(at);
            await db.SaveChangesAsync(ct);
            jobs.Schedule<AnnouncementJobs>(PublishKey(announcement.Id), new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)), j => j.PublishAsync(announcement.Id, CancellationToken.None));
            return;
        }

        await PublishNowAsync(announcement, ct);
    }

    /// <summary>Takes the audience snapshot and marks the announcement published (also the body of the scheduled job).</summary>
    public async Task PublishNowAsync(Announcement announcement, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var audience = await AudienceAsync(announcement, ct);
        db.Set<AnnouncementRecipient>().AddRange(
            audience.Users.Select(u => new AnnouncementRecipient(announcement.OrganizationId, announcement.Id, u.Key, u.Value, now)));
        announcement.MarkPublished(now, audience.Count);
        await db.SaveChangesAsync(ct);
        if (announcement.ExpiresAtUtc is { } expires)
        {
            jobs.Schedule<AnnouncementJobs>(ExpireKey(announcement.Id), new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc)), j => j.ExpireAsync(announcement.Id, null, CancellationToken.None));
        }
    }

    /// <summary>Moves the pending publish job of a scheduled announcement after an edit of its publish time.</summary>
    public void Reschedule(Announcement announcement)
    {
        if (announcement.PublishAtUtc is { } at)
        {
            jobs.Schedule<AnnouncementJobs>(PublishKey(announcement.Id), new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)), j => j.PublishAsync(announcement.Id, CancellationToken.None));
        }
    }

    public async Task WithdrawAsync(Announcement announcement, CancellationToken ct)
    {
        announcement.Withdraw();
        await db.SaveChangesAsync(ct);
        jobs.Cancel(PublishKey(announcement.Id));
        jobs.Cancel(ExpireKey(announcement.Id));
    }
}
