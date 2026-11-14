using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging;

/// <summary>A client and the path of its root unit, where its retention settings are read (client level, root only).</summary>
public sealed record RetentionClient(Guid OrganizationId, string RootPath);

/// <summary>
/// Shared by the messaging retention jobs (D66, D71, L23): the clients to work on, and the three retention values from the settings so the legal answer (O2) changes data and not code.
/// </summary>
public sealed class RetentionSupport(LmsDbContext db, ISettingsResolver settings)
{
    public const int DefaultYearsAfterClose = 1;
    public const int DefaultRemovedOriginalDays = 180;
    public const int DefaultReportContextDays = 180;

    /// <summary>The platform context lists the clients; every job then works on one client at a time (L23), never across clients.</summary>
    public async Task<IReadOnlyList<RetentionClient>> ClientsAsync(CancellationToken ct) =>
        await db.Set<OrgUnit>().Where(u => u.ParentId == null).Select(u => new RetentionClient(u.OrganizationId, u.Path)).ToListAsync(ct);

    public Task<int> YearsAfterCloseAsync(RetentionClient client, CancellationToken ct) => ResolveAsync(client, SettingKeys.MessagingRetentionYearsAfterClose, ct);

    public Task<int> RemovedOriginalDaysAsync(RetentionClient client, CancellationToken ct) => ResolveAsync(client, SettingKeys.MessagingRemovedOriginalRetentionDays, ct);

    public Task<int> ReportContextDaysAsync(RetentionClient client, CancellationToken ct) => ResolveAsync(client, SettingKeys.ModerationReportContextRetentionDays, ct);

    private async Task<int> ResolveAsync(RetentionClient client, SettingKey<int> key, CancellationToken ct) => (await settings.ResolveAsync(key, client.RootPath, ct)).Value;

    public static DateTime StartOfDayUtc(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}
