using System.Text.Json;
using Lms.Client.Services.Attendance;
using Microsoft.JSInterop;

namespace Lms.Client.Tests.Attendance;

/// <summary>A tiny localStorage behind IJSRuntime, so the draft store can be tested without a browser.</summary>
public sealed class StorageJs : IJSRuntime
{
    public Dictionary<string, string> Items { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        object? result = null;
        switch (identifier)
        {
            case "localStorage.getItem": result = Items.GetValueOrDefault((string)args![0]!); break;
            case "localStorage.setItem": Items[(string)args![0]!] = (string)args[1]!; break;
            case "localStorage.removeItem": Items.Remove((string)args![0]!); break;
            case "lmsAttendanceDrafts.clearAll":
                foreach (var key in Items.Keys.Where(k => k.StartsWith((string)args![0]!, StringComparison.Ordinal)).ToList()) Items.Remove(key);
                break;
        }

        return new ValueTask<TValue>((TValue)result!);
    }
}

public sealed class AttendanceDraftStoreTests
{
    private static readonly Guid Session = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid Student = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private readonly StorageJs _js = new();
    private readonly Clock _clock = new();

    private AttendanceDraftStore Store() => new(_js, _clock);

    [Fact]
    public async Task Draft_stores_only_ids_and_statuses()
    {
        await Store().SaveAsync(Session, 3, new Dictionary<Guid, string> { [Student] = "Late" });

        var raw = Assert.Single(_js.Items).Value;
        using var json = JsonDocument.Parse(raw);
        Assert.Equal(["Version", "SavedAtUnixMs", "Marks"], json.RootElement.EnumerateObject().Select(p => p.Name).ToList()); // no names, no notes
        Assert.DoesNotContain("Ahmed", raw);
        Assert.Equal("Late", json.RootElement.GetProperty("Marks").GetProperty(Student.ToString()).GetString());
    }

    [Fact]
    public async Task A_fresh_draft_for_the_same_version_is_loaded()
    {
        await Store().SaveAsync(Session, 3, new Dictionary<Guid, string> { [Student] = "Absent" });

        var marks = await Store().LoadAsync(Session, 3);

        Assert.Equal("Absent", marks![Student]);
    }

    [Fact]
    public async Task Draft_expires_and_is_cleared_on_save_and_sign_out()
    {
        await Store().SaveAsync(Session, 3, new Dictionary<Guid, string> { [Student] = "Absent" });
        _clock.Advance(AttendanceDraftStore.Lifetime + TimeSpan.FromMinutes(1));
        Assert.Null(await Store().LoadAsync(Session, 3)); // expired after 12 hours
        Assert.Empty(_js.Items); // and removed

        await Store().SaveAsync(Session, 3, new Dictionary<Guid, string> { [Student] = "Absent" });
        await Store().ClearAsync(Session); // what a save does
        Assert.Empty(_js.Items);

        await Store().SaveAsync(Session, 3, new Dictionary<Guid, string> { [Student] = "Absent" });
        await Store().SaveAsync(Guid.NewGuid(), 1, new Dictionary<Guid, string> { [Student] = "Present" });
        _js.Items["unrelated"] = "keep";
        await Store().ClearAllAsync(); // sign-out
        Assert.Equal(["unrelated"], _js.Items.Keys);
    }

    [Fact]
    public async Task Stale_draft_never_overwrites_a_newer_server_state()
    {
        await Store().SaveAsync(Session, 3, new Dictionary<Guid, string> { [Student] = "Absent" });

        Assert.Null(await Store().LoadAsync(Session, 4)); // the server moved on: the draft is not offered
        Assert.Empty(_js.Items);
    }

    [Fact]
    public async Task An_unreadable_draft_is_dropped_and_an_empty_one_is_not_kept()
    {
        _js.Items["lms.att.draft." + Session] = "{not json";
        Assert.Null(await Store().LoadAsync(Session, 1));
        Assert.Empty(_js.Items);

        await Store().SaveAsync(Session, 1, new Dictionary<Guid, string>());
        Assert.Empty(_js.Items);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
