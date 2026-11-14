using System.Text.Json;
using Lms.Client.Services.Api;
using Lms.Shared.Configuration;

namespace Lms.Client.Services;

/// <summary>
/// The client's time zone (the <c>Org.TimeZone</c> setting, D64), read once per active context from the settings of the context's unit. A caller
/// who may not read settings, or a failed read, gets Asia/Riyadh (the default), so a session day is never judged by the UTC date.
/// </summary>
public sealed class ClientTimeZone(ILmsApi me, IConfigApi config, IContextStore context)
{
    private Guid? _for;
    private TimeZoneInfo? _zone;

    public async Task<TimeZoneInfo> GetAsync(CancellationToken ct = default)
    {
        var active = context.ActiveAssignmentId;
        if (_zone is not null && _for == active) return _zone;

        string? id = null;
        try
        {
            var mine = await me.GetMeAsync(ct);
            if (mine.Contexts.FirstOrDefault(c => c.AssignmentId == active) is { } ctx)
            {
                var settings = await config.GetSettingsIfAllowedAsync(ctx.OrgUnitId, ct);
                id = settings.FirstOrDefault(s => s.Name == SettingKeys.OrgTimeZone.Name) is { } s ? JsonSerializer.Deserialize<string>(s.ValueJson) : null;
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or JsonException) { }

        _zone = ClientCalendar.Zone(string.IsNullOrWhiteSpace(id) ? null : id);
        _for = active;
        return _zone;
    }

    /// <summary>The client's local day of an instant.</summary>
    public async Task<DateOnly> TodayAsync(DateTime utc, CancellationToken ct = default) => ClientCalendar.Today(utc, await GetAsync(ct));
}
