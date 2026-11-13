using Bunit;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared.Packages;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Lms.Client.Tests.Packages;

/// <summary>The page side of the message bridge: what is accepted, coalesced, throttled, flushed and never trusted (ADR 0005 section 4).</summary>
public sealed class PackageBridgeTests : BunitContext
{
    private readonly FakePackagePlayerApi _api = new();
    private readonly ManualClock _clock = new();
    private readonly Guid _attempt = Guid.NewGuid();
    private readonly PackageBridgeOptions _options = new() { MinCommitInterval = TimeSpan.FromSeconds(5), AutoRetries = 1, RetryDelay = TimeSpan.FromMilliseconds(1) };

    public PackageBridgeTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private PackageBridge NewBridge(bool preview = false) => new(JSInterop.JSRuntime, _api, _attempt, preview, _options, _clock);

    private static Dictionary<string, string> Data(params (string Key, string Value)[] items) => items.ToDictionary(i => i.Key, i => i.Value);

    [Fact]
    public async Task Commits_carry_seq_and_are_throttled_and_coalesced()
    {
        var bridge = NewBridge();

        await bridge.OnPortMessage("commit", 1, "Scorm", Data(("cmi.core.lesson_location", "p1")));
        _clock.Advance(TimeSpan.Zero);
        await WaitForCommitsAsync(1);
        await bridge.OnPortMessage("commit", 2, "Scorm", Data(("cmi.core.lesson_location", "p2"), ("cmi.suspend_data", "s")));
        await bridge.OnPortMessage("commit", 3, "Scorm", Data(("cmi.core.score.raw", "80")));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(_api.Commits); // still inside the interval: the later messages wait
        _clock.Advance(TimeSpan.FromSeconds(5));
        await WaitForCommitsAsync(2);

        var second = _api.Commits[1].Request;
        Assert.Equal(3, second.Seq);
        Assert.Equal("p2", second.Data["cmi.core.lesson_location"]);
        Assert.Equal("s", second.Data["cmi.suspend_data"]);
        Assert.Equal("80", second.Data["cmi.core.score.raw"]);
    }

    private async Task WaitForCommitsAsync(int count)
    {
        for (var i = 0; i < 200 && _api.Commits.Count < count; i++) await Task.Delay(10); // the timer callback sends on a pool thread
        Assert.Equal(count, _api.Commits.Count);
    }

    [Fact]
    public async Task Queued_commits_from_before_the_handshake_are_forwarded_in_seq_order()
    {
        var bridge = NewBridge();

        // The shim queued three changes before the port existed and the messages arrive out of order: the highest seq wins per key.
        await bridge.OnPortMessage("commit", 3, "Scorm", Data(("cmi.suspend_data", "third")));
        await bridge.OnPortMessage("commit", 1, "Scorm", Data(("cmi.suspend_data", "first"), ("cmi.core.lesson_location", "a")));
        await bridge.OnPortMessage("commit", 2, "Scorm", Data(("cmi.suspend_data", "second")));
        await bridge.FlushAsync();

        var sent = Assert.Single(_api.Commits).Request;
        Assert.Equal(3, sent.Seq);
        Assert.Equal("third", sent.Data["cmi.suspend_data"]);
        Assert.Equal("a", sent.Data["cmi.core.lesson_location"]);
    }

    [Theory]
    [InlineData("hack", 1, false)]
    [InlineData("commit", -1, false)]
    [InlineData("commit", 1, true)]
    [InlineData("finish", 7, true)]
    public void Only_commit_and_finish_with_a_non_negative_seq_are_accepted(string type, long seq, bool accepted) =>
        Assert.Equal(accepted, PackageBridge.IsAcceptable(type, seq, Data(("k", "v"))));

    [Fact]
    public async Task Malformed_or_oversize_messages_are_ignored()
    {
        var bridge = NewBridge();
        var bigValue = new string('x', PackageTrackingLimits.MaxValueBytes + 1);
        var manyValues = Enumerable.Range(0, 9).ToDictionary(i => $"k{i}", _ => new string('y', 1000)); // 9 KB in one message

        await bridge.OnPortMessage("exec", 1, "Scorm", Data(("k", "v")));
        await bridge.OnPortMessage("commit", -5, "Scorm", Data(("k", "v")));
        await bridge.OnPortMessage("commit", 2, "Scorm", Data(("k", bigValue)));
        await bridge.OnPortMessage("commit", 3, "Scorm", manyValues);
        await bridge.FlushAsync();

        Assert.Empty(_api.Commits);
        Assert.False(bridge.HasPending);
    }

    [Fact]
    public async Task Window_messages_never_trigger_a_commit_even_from_the_player_frame()
    {
        var bridge = NewBridge();

        bridge.OnWindowMessageDropped();
        bridge.OnWindowMessageDropped();
        await bridge.FlushAsync();

        Assert.Equal(2, bridge.DroppedWindowMessages);
        Assert.Empty(_api.Commits);
        Assert.False(bridge.HasPending);
    }

    [Fact]
    public async Task Attempt_id_comes_from_the_launch_not_from_the_message()
    {
        var bridge = NewBridge();
        var forged = Guid.NewGuid().ToString();

        await bridge.OnPortMessage("commit", 1, "Scorm", Data(("attemptId", forged), ("cmi.suspend_data", "s")));
        await bridge.FlushAsync();

        var (attemptId, request) = Assert.Single(_api.Commits);
        Assert.Equal(_attempt, attemptId);
        Assert.Equal(forged, request.Data["attemptId"]); // it is only data: it never becomes an id or a route
    }

    [Fact]
    public async Task Pagehide_flushes_the_last_state()
    {
        var bridge = NewBridge();
        await bridge.OnPortMessage("commit", 4, "Scorm", Data(("cmi.suspend_data", "last")));

        await bridge.OnPageHide();

        Assert.Equal(4, Assert.Single(_api.Commits).Request.Seq); // sent at once, without waiting for the interval
    }

    [Fact]
    public async Task Save_failure_shows_state_and_retries()
    {
        var bridge = NewBridge();
        _api.CommitFailure = new HttpRequestException("offline");
        await bridge.OnPortMessage("commit", 1, "Scorm", Data(("cmi.suspend_data", "s")));

        await bridge.FlushAsync();

        Assert.Equal(PackageSaveStatus.Failed, bridge.Status);
        Assert.True(bridge.HasPending); // kept for the retry
        _api.CommitFailure = null;
        await bridge.RetryAsync();
        Assert.Equal(PackageSaveStatus.Saved, bridge.Status);
        Assert.Equal("s", Assert.Single(_api.Commits).Request.Data["cmi.suspend_data"]);
    }

    [Fact]
    public async Task A_preview_sends_nothing()
    {
        var bridge = NewBridge(preview: true);

        await bridge.OnPortMessage("commit", 1, "Scorm", Data(("cmi.suspend_data", "s")));
        await bridge.FlushAsync();

        Assert.Empty(_api.Commits);
        Assert.Equal(PackageSaveStatus.Idle, bridge.Status);
    }

    [Fact]
    public async Task Port_and_listeners_are_removed_on_dispose()
    {
        var bridge = NewBridge();
        await bridge.StartAsync(new ElementReference("frame"));

        await bridge.DisposeAsync();

        Assert.Single(JSInterop.Invocations["lmsPackageBridge.attach"]);
        Assert.Single(JSInterop.Invocations["lmsPackageBridge.dispose"]);
    }

    [Fact]
    public async Task Nothing_sensitive_is_handed_to_the_script()
    {
        var bridge = NewBridge();
        await bridge.StartAsync(new ElementReference("frame"));
        await bridge.DisposeAsync();

        // The bridge talks to its script only to attach (the frame element and itself) and to dispose: no token, id, name or answer.
        Assert.All(JSInterop.Invocations, i => Assert.StartsWith("lmsPackageBridge.", i.Identifier));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Arguments.Any(a => a is string or Guid));
    }

    // ---- the script itself (the only JS that touches package messages) ----------------------------------------------------

    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Lms.Client", "wwwroot", "js", "package-bridge.js"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Lms.Client", "wwwroot", "js", "package-bridge.js"));
    }

    [Fact]
    public void Origin_null_is_not_used_as_a_trust_signal()
    {
        var code = string.Join('\n', Script().Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("event.origin", code);
        Assert.DoesNotContain(".origin ===", code);
        Assert.DoesNotContain(".origin ==", code);
    }

    [Fact]
    public void The_handshake_posts_only_a_fixed_non_sensitive_message_and_the_port()
    {
        var script = Script();

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(script, @"contentWindow\.postMessage\("));
        Assert.Contains("postMessage({ lms: 'package-port', v: 1 }, '*', [channel.port2])", script);
    }

    [Fact]
    public void The_script_never_renders_messages_as_html()
    {
        var script = Script();

        Assert.DoesNotContain("innerHTML", script);
        Assert.DoesNotContain("insertAdjacentHTML", script);
        Assert.DoesNotContain("document.write", script);
        Assert.DoesNotContain("eval(", script);
    }
}
