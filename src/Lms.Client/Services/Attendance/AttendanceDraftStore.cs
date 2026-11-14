using System.Text.Json;
using Microsoft.JSInterop;

namespace Lms.Client.Services.Attendance;

/// <summary>
/// A per-viewer convenience: the marks of a session being taken survive a lost connection or a reload (spike S4, L25). It keeps only
/// the session id, the student ids with their statuses and the session <c>Version</c> it was made against (never names or notes), expires after
/// 12 hours, is removed when the session is saved, and a draft made against an older version never overwrites a newer server state.
/// </summary>
public sealed class AttendanceDraftStore(IJSRuntime js, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
    public const string Prefix = "lms.att.draft.";

    private sealed record Draft(int Version, long SavedAtUnixMs, Dictionary<Guid, string> Marks);

    public async Task SaveAsync(Guid sessionId, int version, IReadOnlyDictionary<Guid, string> marks)
    {
        if (marks.Count == 0)
        {
            await ClearAsync(sessionId);
            return;
        }

        var draft = new Draft(version, clock.GetUtcNow().ToUnixTimeMilliseconds(), marks.ToDictionary(m => m.Key, m => m.Value));
        await TrySetAsync(Prefix + sessionId, JsonSerializer.Serialize(draft));
    }

    /// <summary>The saved marks when a fresh draft for exactly this server version exists; an expired, older or unreadable one is removed.</summary>
    public async Task<IReadOnlyDictionary<Guid, string>?> LoadAsync(Guid sessionId, int serverVersion)
    {
        var raw = await TryGetAsync(Prefix + sessionId);
        if (raw is null) return null;
        Draft? draft = null;
        try { draft = JsonSerializer.Deserialize<Draft>(raw); } catch (JsonException) { }

        var age = draft is null ? TimeSpan.MaxValue : clock.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(draft.SavedAtUnixMs);
        if (draft is null || age > Lifetime || draft.Version != serverVersion)
        {
            await ClearAsync(sessionId);
            return null;
        }

        return draft.Marks;
    }

    public Task ClearAsync(Guid sessionId) => TryRemoveAsync(Prefix + sessionId);

    /// <summary>Removes every draft (sign-out).</summary>
    public async Task ClearAllAsync()
    {
        try
        {
            await js.InvokeVoidAsync("lmsAttendanceDrafts.clearAll", Prefix);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { }
    }

    private async Task<string?> TryGetAsync(string key)
    {
        try { return await js.InvokeAsync<string?>("localStorage.getItem", key); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { return null; }
    }

    private async Task TrySetAsync(string key, string value)
    {
        try { await js.InvokeVoidAsync("localStorage.setItem", key, value); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { }
    }

    private async Task TryRemoveAsync(string key)
    {
        try { await js.InvokeVoidAsync("localStorage.removeItem", key); }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException) { }
    }
}
