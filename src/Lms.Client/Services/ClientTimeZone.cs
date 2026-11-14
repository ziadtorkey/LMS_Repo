using Lms.Client.Services.Api;

namespace Lms.Client.Services;

/// <summary>
/// The client's time zone (D64): the <c>TimeZone</c> of the active context in <c>GET /api/me</c> (the client's <c>Org.TimeZone</c>, readable by every
/// role, no Settings.View needed). A failed read or an unknown id falls back to Asia/Riyadh, so a session day is never judged by the UTC date.
/// </summary>
public sealed class ClientTimeZone(ILmsApi me, IContextStore context)
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
            id = mine.Contexts.FirstOrDefault(c => c.AssignmentId == active)?.TimeZone;
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException) { }

        _zone = ClientCalendar.Zone(string.IsNullOrWhiteSpace(id) ? null : id);
        _for = active;
        return _zone;
    }

    /// <summary>The client's local day of an instant.</summary>
    public async Task<DateOnly> TodayAsync(DateTime utc, CancellationToken ct = default) => ClientCalendar.Today(utc, await GetAsync(ct));
}
