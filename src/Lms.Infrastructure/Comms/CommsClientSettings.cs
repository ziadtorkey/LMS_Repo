using System.Globalization;
using Lms.Domain.Comms;
using Lms.Infrastructure.Configuration;
using Lms.Domain.Organizations;
using Lms.Shared.Configuration;
using Lms.Shared.Notifications;

namespace Lms.Infrastructure.Comms;

/// <summary>The client-level communication settings (D66) a delivery decision needs, read once per client.</summary>
public sealed record CommsSettings(
    bool EmailEnabled,
    bool PushEnabled,
    int MaxEmailPerDay,
    int MaxPushPerDay,
    int MaxEmailPerUserPerDay,
    int MaxPushPerUserPerDay,
    bool QuietHoursEnabled,
    TimeOnly QuietStart,
    TimeOnly QuietEnd,
    int RetentionDays,
    TimeZoneInfo Zone)
{
    public bool IsEnabled(string channel) => channel == NotificationChannels.Email ? EmailEnabled : channel == NotificationChannels.Push && PushEnabled;

    public int ClientCap(string channel) => channel == NotificationChannels.Email ? MaxEmailPerDay : MaxPushPerDay;

    public int UserCap(string channel) => channel == NotificationChannels.Email ? MaxEmailPerUserPerDay : MaxPushPerUserPerDay;

    /// <summary>When the client's quiet hours end if <paramref name="utcNow"/> lies inside them; null otherwise.</summary>
    public DateTime? QuietUntil(DateTime utcNow) =>
        QuietHoursEnabled && DeliveryPolicy.IsQuiet(utcNow, Zone, QuietStart, QuietEnd, out var end) ? end : null;
}

/// <summary>Reads <see cref="CommsSettings"/> at the client root (every <c>Comms.*</c> key is client level, edited there by the Org Admin).</summary>
public sealed class CommsClientSettings(IOrgTree orgTree, ISettingsResolver settings)
{
    public async Task<CommsSettings> GetAsync(Guid organizationId, CancellationToken ct)
    {
        var root = (await orgTree.GetRootAsync(organizationId, ct)).Path;

        async Task<T> Get<T>(SettingKey<T> key) => (await settings.ResolveAsync(key, root, ct)).Value;

        return new CommsSettings(
            await Get(SettingKeys.CommsEmailEnabled),
            await Get(SettingKeys.CommsPushEnabled),
            await Get(SettingKeys.CommsMaxEmailPerDay),
            await Get(SettingKeys.CommsMaxPushPerDay),
            await Get(SettingKeys.CommsMaxEmailPerUserPerDay),
            await Get(SettingKeys.CommsMaxPushPerUserPerDay),
            await Get(SettingKeys.CommsQuietHoursEnabled),
            ParseTime(await Get(SettingKeys.CommsQuietHoursStart), new TimeOnly(21, 0)),
            ParseTime(await Get(SettingKeys.CommsQuietHoursEnd), new TimeOnly(7, 0)),
            await Get(SettingKeys.CommsDeliveryRetentionDays),
            Zone(await Get(SettingKeys.OrgTimeZone)));
    }

    private static TimeOnly ParseTime(string value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : fallback;

    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
