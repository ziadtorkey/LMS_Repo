using System.Globalization;
using Lms.Domain.Abstractions;
using Lms.Domain.Notifications;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Jobs;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Lms.Infrastructure.People.Staff;

/// <summary>
/// Daily staff expiry (FR-STF-06, D35), run as a system job (platform context) per client: profiles whose last usable
/// day <c>ExpiresOn</c> has passed become Expired and their admins above are notified; profiles about to expire get one
/// notice, <c>Staff.ExpiryNoticeDays</c> (resolved at the home unit) before the last day. Access already stops at the
/// start of <c>ExpiresOn + 1</c> through the status gate; this job only makes the status and the notices follow.
/// Idempotent per day.
/// </summary>
public sealed class StaffExpiryJob(
    LmsDbContext db,
    IPersonStatusService status,
    INotificationPublisher publisher,
    ISettingsResolver settings,
    IClock clock,
    JobSettings jobSettings,
    ILogger<StaffExpiryJob> logger)
{
    public const string RecurringJobId = "staff-expiry";

    /// <summary>Every day at 02:00 in <c>Jobs:TimeZone</c> (UTC by default, D35).</summary>
    public const string Cron = "0 2 * * *";

    public const string ExpiredReason = "Expired automatically";

    /// <param name="asOf">The day to evaluate; default today in <c>Jobs:TimeZone</c>. The dev trigger passes it for E2E.</param>
    public async Task RunAsync(DateOnly? asOf, CancellationToken cancellationToken)
    {
        var day = asOf ?? jobSettings.Today(clock);
        // The platform context sees every client; the list of clients is platform-level data.
        var organizationIds = await db.Set<Organization>().Select(o => o.Id).ToListAsync(cancellationToken);
        foreach (var organizationId in organizationIds)
        {
            try
            {
                await ProcessOrganizationAsync(organizationId, day, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One client's failure must not stop the others; the next run retries it (the job is idempotent).
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Staff expiry failed for client {OrganizationId}", organizationId);
            }
        }
    }

    private async Task ProcessOrganizationAsync(Guid organizationId, DateOnly day, CancellationToken ct)
    {
        var expired = await db.Set<StaffProfile>()
            .Where(p => p.OrganizationId == organizationId && p.Status == PersonStatus.Active && p.ExpiresOn != null && p.ExpiresOn < day)
            .ToListAsync(ct);
        foreach (var profile in expired)
        {
            await status.ChangeAsync(PersonKind.Staff, profile.Id, PersonStatus.Expired, ExpiredReason, ct);
            await PublishAsync(NotificationEvents.StaffExpired, profile, ct);
        }

        var candidates = await db.Set<StaffProfile>()
            .Where(p => p.OrganizationId == organizationId && p.Status == PersonStatus.Active && p.ExpiresOn != null
                        && p.ExpiresOn >= day && p.ExpiryNoticeSentOn == null)
            .ToListAsync(ct);
        var daysByUnit = new Dictionary<string, int>();
        foreach (var profile in candidates)
        {
            if (!daysByUnit.TryGetValue(profile.OrgUnitPath, out var noticeDays))
            {
                noticeDays = (await settings.ResolveAsync(SettingKeys.StaffExpiryNoticeDays, profile.OrgUnitPath, ct)).Value;
                daysByUnit[profile.OrgUnitPath] = noticeDays;
            }

            if (noticeDays <= 0 || day < profile.ExpiresOn!.Value.AddDays(-noticeDays))
            {
                continue; // notices are off, or it is too early
            }

            profile.MarkExpiryNoticeSent(day);
            await db.SaveChangesAsync(ct); // marked first: a failed notice is skipped rather than repeated every day
            await PublishAsync(NotificationEvents.StaffExpiringSoon, profile, ct);
        }
    }

    private Task PublishAsync(string eventKey, StaffProfile profile, CancellationToken ct) =>
        publisher.PublishAsync(
            new NotificationRequest(
                eventKey, profile.OrganizationId, [],
                new Dictionary<string, string>
                {
                    ["StaffNameAr"] = profile.NameAr,
                    ["StaffNameEn"] = profile.NameEn,
                    ["ExpiresOn"] = profile.ExpiresOn!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
                DeepLink: $"/staff/{profile.Id}",
                OrgUnitPath: profile.OrgUnitPath),
            ct);
}
