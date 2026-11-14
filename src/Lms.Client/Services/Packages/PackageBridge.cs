using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Lms.Client.Services.Api;
using Lms.Shared.Packages;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lms.Client.Services.Packages;

/// <summary>What the player shows next to the title while a package reports progress (design spec §5.2).</summary>
public enum PackageSaveStatus { Idle, Saving, Saved, Failed }

/// <summary>Timing of the commits the page makes for a package. The server enforces the real rate (<c>Packages.CommitMinIntervalSeconds</c>).</summary>
public sealed class PackageBridgeOptions
{
    /// <summary>The shortest time between two commit calls; the messages in between are coalesced into one.</summary>
    public TimeSpan MinCommitInterval { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>A failed commit is retried this many times with a growing delay, then waits for the person to press Retry.</summary>
    public int AutoRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// The .NET side of one player's message bridge (ADR 0005 section 4; the JS side is wwwroot/js/package-bridge.js). The frame talks to the
/// page only over one private <c>MessagePort</c>; window messages are counted and dropped. Port messages are untrusted data: they are
/// validated again here (type, seq, sizes), never rendered and never used as an id. This class is the one place that turns them into
/// commit calls: coalesced (the latest value of every key wins, in <c>seq</c> order), throttled, flushed before a finish or a
/// re-launch and on page hide. The attempt id comes from the launch, never from a message. In a preview nothing is sent.
/// </summary>
public sealed class PackageBridge : IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private readonly IPackagePlayerApi _api;
    private readonly PackageBridgeOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SortedDictionary<long, Dictionary<string, string>> _pending = [];
    private DotNetObjectReference<PackageBridge>? _self;
    private ITimer? _timer;
    private int _jsId;
    private long _lastSentSeq = -1;
    private long _highestSeq = -1;
    private DateTimeOffset _lastSentAt = DateTimeOffset.MinValue;
    private string _format = PackageCommitFormats.Scorm;
    private bool _disposed;

    public PackageBridge(IJSRuntime js, IPackagePlayerApi api, Guid attemptId, bool preview, PackageBridgeOptions options, TimeProvider clock)
    {
        _js = js;
        _api = api;
        AttemptId = attemptId;
        Preview = preview;
        _options = options;
        _clock = clock;
    }

    public Guid AttemptId { get; }
    public bool Preview { get; }
    public PackageSaveStatus Status { get; private set; } = PackageSaveStatus.Idle;
    public int DroppedWindowMessages { get; private set; }
    public long LastSeq => Math.Max(_highestSeq, _lastSentSeq);
    public bool HasPending => _pending.Count > 0;

    /// <summary>Raised when the save status changes, and when the content reports <c>finish</c> (the player then closes the attempt).</summary>
    public event Action? StatusChanged;
    public event Func<Task>? FinishRequested;

    /// <summary>Starts listening: the port is transferred to the frame when it has loaded.</summary>
    public async Task StartAsync(ElementReference iframe)
    {
        _self = DotNetObjectReference.Create(this);
        _jsId = await _js.InvokeAsync<int>("lmsPackageBridge.attach", iframe, _self);
    }

    /// <summary>The window-level message handler of the JS side: a message that did not come over the port. Counted, never acted on.</summary>
    [JSInvokable]
    public void OnWindowMessageDropped() => DroppedWindowMessages++;

    /// <summary>A message that came over the private port (already shape-checked in JS, checked again here).</summary>
    [JSInvokable]
    public async Task OnPortMessage(string type, long seq, string? format, Dictionary<string, string>? data)
    {
        if (_disposed || !IsAcceptable(type, seq, data, format == PackageCommitFormats.Xapi ? PackageCommitFormats.Xapi : PackageCommitFormats.Scorm)) return;
        _format = format == PackageCommitFormats.Xapi ? PackageCommitFormats.Xapi : PackageCommitFormats.Scorm;
        _highestSeq = Math.Max(_highestSeq, seq);
        if (!Preview)
        {
            // A message older than one already sent changes nothing on the server (it keeps the highest seq), so it is not queued.
            if (seq > _lastSentSeq) _pending[seq] = data is null ? [] : new Dictionary<string, string>(data);
            Schedule();
        }

        if (type == "finish" && FinishRequested is { } handler) await handler();
    }

    /// <summary>The tab is closing or being hidden: send what is pending now (the frame cannot use sendBeacon).</summary>
    [JSInvokable]
    public async Task OnPageHide() => await FlushAsync();

    /// <summary>The checks that apply to every port message (see also the JS side).</summary>
    private static readonly JsonSerializerOptions SizeOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static bool IsAcceptable(string type, long seq, Dictionary<string, string>? data, string format = PackageCommitFormats.Scorm)
    {
        if (type is not ("commit" or "finish") || seq < 0) return false;
        if (data is null) return true;
        if (data.Count > 256) return false;
        foreach (var value in data.Values)
            if (Encoding.UTF8.GetByteCount(value) > PackageTrackingLimits.MaxValueBytes) return false;
        return JsonSerializer.SerializeToUtf8Bytes(new { type, seq, format, data }, SizeOptions).Length <= PackageTrackingLimits.MaxMessageBytes; // the same UTF-8 bytes the script counts (JSON.stringify does not escape non-ASCII);
    }

    private void Schedule()
    {
        if (_pending.Count == 0 || _timer is not null) return;
        var due = _lastSentAt + _options.MinCommitInterval - _clock.GetUtcNow();
        _timer = _clock.CreateTimer(_ => _ = FireAsync(), null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private async Task FireAsync()
    {
        _timer?.Dispose();
        _timer = null;
        await SendPendingAsync();
    }

    /// <summary>Sends every pending change now and waits for the server's answer (the drain before a finish or a re-launch).</summary>
    public async Task FlushAsync()
    {
        _timer?.Dispose();
        _timer = null;
        await SendPendingAsync();
    }

    private async Task SendPendingAsync()
    {
        await _sendLock.WaitAsync();
        try
        {
            if (_pending.Count == 0 || Preview) return;
            // Coalesce in seq order: the value of a key that came later wins, whatever order the messages arrived in.
            var merged = new Dictionary<string, string>();
            foreach (var (_, data) in _pending)
                foreach (var (key, value) in data)
                    merged[key] = value;
            var seq = _pending.Keys.Max();
            _pending.Clear();
            SetStatus(PackageSaveStatus.Saving);
            if (await SendWithRetriesAsync(new PackageCommitRequest(seq, _format, merged)))
            {
                _lastSentSeq = Math.Max(_lastSentSeq, seq);
                _lastSentAt = _clock.GetUtcNow();
                SetStatus(PackageSaveStatus.Saved);
            }
            else
            {
                // Keep what could not be saved: the next message (or Retry) sends it together with the newer data.
                _pending[seq] = merged;
                SetStatus(PackageSaveStatus.Failed);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<bool> SendWithRetriesAsync(PackageCommitRequest request)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await _api.CommitAsync(AttemptId, request);
                return true;
            }
            catch (Exception ex) when (ex is ApiProblemException or HttpRequestException)
            {
                if (attempt >= _options.AutoRetries || ex is ApiProblemException { Status: 409 }) return false;
                await Task.Delay(TimeSpan.FromTicks(_options.RetryDelay.Ticks * (attempt + 1)));
            }
        }
    }

    /// <summary>The "Retry" of a failed save.</summary>
    public Task RetryAsync() => FlushAsync();

    private void SetStatus(PackageSaveStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        // Leaving the page inside the app (not pagehide) must not drop what still waits for the throttle: a best-effort flush first.
        if (!Preview && _pending.Count > 0)
        {
            try { await FlushAsync(); }
            catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or ObjectDisposedException) { }
        }
        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        try { await _js.InvokeVoidAsync("lmsPackageBridge.dispose", _jsId); }
        catch (JSDisconnectedException) { }
        _self?.Dispose();
    }
}
