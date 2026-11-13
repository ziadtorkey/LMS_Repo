using System.Globalization;
using Bunit;
using Lms.Client.Components.Packages;
using Lms.Client.Services.Api;
using Lms.Client.Services.Packages;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Content;
using Lms.Shared.Packages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;

namespace Lms.Client.Tests.Packages;

/// <summary>The sandboxed package player (M4b Task 17, ADR 0005): the frame, its bridge, the token's life and the states around it.</summary>
public sealed class PackagePlayerTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid PackageId = Guid.NewGuid();
    private static readonly Guid ClassCourse = Guid.NewGuid();

    private readonly FakePackagePlayerApi _api = new();
    private readonly ManualClock _clock = new();
    private readonly PackageBridgeOptions _options = new() { MinCommitInterval = TimeSpan.Zero, AutoRetries = 0, RetryDelay = TimeSpan.FromMilliseconds(1) };
    private int _finished;

    public PackagePlayerTests()
    {
        _api.ServerNow = () => _clock.GetUtcNow().UtcDateTime;
        Services.AddSingleton<IPackagePlayerApi>(_api);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton(_options);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<PackagePlayer>> RenderPlayerAsync(string? state = null, bool staff = false, Guid? packageId = null)
    {
        await GrantAsync(staff ? [Permissions.Packages.View, Permissions.Modules.Manage] : [Permissions.Packages.View, Permissions.Modules.View]);
        Render<MudPopoverProvider>();
        return Render<PackagePlayer>(p => p
            .Add(x => x.PackageId, packageId ?? PackageId).Add(x => x.ClassCourseId, ClassCourse).Add(x => x.Title, "Fractions lab").Add(x => x.State, state)
            .Add(x => x.OnFinished, Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, () => _finished++)));
    }

    private async Task<IRenderedComponent<PackagePlayer>> StartedAsync(string? state = null)
    {
        var player = await RenderPlayerAsync(state);
        player.Find("[data-testid=package-start]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);
        return player;
    }

    /// <summary>The bridge the player created: it is the .NET object handed to the script's attach call.</summary>
    private PackageBridge Bridge(int nth = -1)
    {
        var calls = JSInterop.Invocations["lmsPackageBridge.attach"].ToList();
        var call = nth < 0 ? calls[^1] : calls[nth];
        return ((DotNetObjectReference<PackageBridge>)call.Arguments[1]!).Value;
    }

    // ---- the frame ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Player_iframe_is_sandboxed_without_same_origin_top_navigation_or_popups()
    {
        var player = await StartedAsync();

        var sandbox = player.Find("[data-testid=package-frame]").GetAttribute("sandbox");

        Assert.Equal("allow-scripts", sandbox); // exactly this and nothing else (ADR 0005 section 3)
        Assert.DoesNotContain("allow-same-origin", sandbox);
        Assert.DoesNotContain("allow-top-navigation", sandbox);
        Assert.DoesNotContain("allow-popups", sandbox);
    }

    [Fact]
    public async Task Player_iframe_has_no_allow_forms()
    {
        var player = await StartedAsync();

        Assert.DoesNotContain("allow-forms", player.Find("[data-testid=package-frame]").GetAttribute("sandbox"));
    }

    [Fact]
    public async Task Player_iframe_has_a_title_and_no_feature_permissions()
    {
        var player = await StartedAsync();
        var frame = player.Find("[data-testid=package-frame]");

        Assert.Contains("Fractions lab", frame.GetAttribute("title"));
        Assert.Equal("", frame.GetAttribute("allow"));
        Assert.Equal("no-referrer", frame.GetAttribute("referrerpolicy"));
        Assert.StartsWith("https://content.example.test/p/", frame.GetAttribute("src"));
    }

    [Fact]
    public async Task Nothing_from_the_package_is_rendered_into_the_page()
    {
        var player = await StartedAsync();

        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.suspend_data"] = "<img src=x onerror=alert(1)>" }));

        Assert.DoesNotContain("onerror", player.Markup);
        Assert.DoesNotContain("<img src=x", player.Markup);
    }

    // ---- the bridge in the player ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Port_is_transferred_after_load_and_only_port_messages_are_accepted()
    {
        var player = await StartedAsync();

        // The script attaches once, when the frame exists; the port goes to the frame on its load event (script side).
        Assert.Single(JSInterop.Invocations["lmsPackageBridge.attach"]);
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.core.lesson_location"] = "p1" }));
        await player.InvokeAsync(() => Bridge().FlushAsync());

        Assert.Single(_api.Commits);
    }

    [Fact]
    public async Task Messages_from_a_window_that_is_not_the_player_frame_are_ignored()
    {
        var player = await StartedAsync();

        // Another window that also reports origin "null": the script counts it and tells .NET it was dropped. Nothing is committed.
        await player.InvokeAsync(() => Bridge().OnWindowMessageDropped());
        await player.InvokeAsync(() => Bridge().FlushAsync());

        Assert.Equal(1, Bridge().DroppedWindowMessages);
        Assert.Empty(_api.Commits);
    }

    [Fact]
    public async Task Attempt_id_comes_from_the_launch_not_from_the_message()
    {
        var player = await StartedAsync();

        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["attempt"] = Guid.NewGuid().ToString() }));
        await player.InvokeAsync(() => Bridge().FlushAsync());

        Assert.Equal(_api.AttemptOf(PackageId), Assert.Single(_api.Commits).AttemptId);
    }

    [Fact]
    public async Task Finish_drains_the_port_then_sends_the_last_seq()
    {
        var player = await StartedAsync();

        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 4, "Scorm", new() { ["cmi.suspend_data"] = "s" }));
        await player.InvokeAsync(() => Bridge().OnPortMessage("finish", 5, "Scorm", new()));

        player.WaitForAssertion(() => Assert.Contains("finish 5", _api.Log), Wait);
        Assert.Equal(["launch", "commit 5", "finish 5"], _api.Log); // the pending commit was drained first, then the barrier
    }

    [Fact]
    public async Task Finish_reloads_the_module_state()
    {
        var player = await StartedAsync();

        await player.InvokeAsync(() => Bridge().OnPortMessage("finish", 1, "Scorm", new()));

        player.WaitForElement("[data-testid=package-result-card]", Wait);
        Assert.Equal(1, _finished); // the viewer is asked to read the module's completion again
        Assert.Contains("85", player.Find("[data-testid=package-result-score]").TextContent); // the server's judgement, not computed here
        Assert.Empty(player.FindAll("[data-testid=package-frame]"));
    }

    [Fact]
    public async Task Save_failure_shows_state_and_retries()
    {
        var player = await StartedAsync();
        _api.CommitFailure = new HttpRequestException("offline");

        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.suspend_data"] = "s" }));
        await player.InvokeAsync(() => Bridge().FlushAsync());

        player.WaitForAssertion(() => Assert.Equal("Failed", player.Find("[data-testid=package-save-state]").GetAttribute("data-status")), Wait);
        _api.CommitFailure = null;
        player.Find("[data-testid=package-save-retry]").Click();
        player.WaitForAssertion(() => Assert.Equal("Saved", player.Find("[data-testid=package-save-state]").GetAttribute("data-status")), Wait);
    }

    [Fact]
    public async Task Pagehide_flushes_the_last_state()
    {
        var player = await StartedAsync();
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 7, "Scorm", new() { ["cmi.suspend_data"] = "s" }));

        await player.InvokeAsync(() => Bridge().OnPageHide());

        Assert.Equal(7, Assert.Single(_api.Commits).Request.Seq);
    }

    [Fact]
    public async Task Port_and_listeners_are_removed_on_dispose()
    {
        var player = await StartedAsync();

        await DisposeComponentsAsync();

        Assert.Single(JSInterop.Invocations["lmsPackageBridge.dispose"]);
        _ = player;
    }

    // ---- the token's life ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Relaunch_swaps_the_frame_only_after_a_flush_and_replaces_the_bridge()
    {
        var player = await StartedAsync();
        var firstSrc = player.Find("[data-testid=package-frame]").GetAttribute("src");
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 9, "Scorm", new() { ["cmi.suspend_data"] = "s" }));

        _clock.Advance(TimeSpan.FromMinutes(97)); // past 80 % of the 120 minute token
        player.WaitForAssertion(() => Assert.Equal(2, _api.Launches.Count), Wait);

        Assert.Equal(["launch", "commit 9", "launch"], _api.Log); // pending progress reached the server before the new token was asked for
        player.WaitForAssertion(() => Assert.NotEqual(firstSrc, player.Find("[data-testid=package-frame]").GetAttribute("src")), Wait);
        player.WaitForAssertion(() => Assert.Equal(2, JSInterop.Invocations["lmsPackageBridge.attach"].Count), Wait);
        player.WaitForAssertion(() => Assert.Single(JSInterop.Invocations["lmsPackageBridge.dispose"]), Wait); // one bridge per player: the old one is gone
        Assert.Equal(_api.AttemptOf(PackageId), Bridge().AttemptId); // the same attempt
    }

    [Fact]
    public async Task A_failed_flush_keeps_the_frame_and_the_progress_instead_of_relaunching()
    {
        var player = await StartedAsync();
        var firstSrc = player.Find("[data-testid=package-frame]").GetAttribute("src");
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 9, "Scorm", new() { ["cmi.suspend_data"] = "keep me" }));
        _api.CommitFailure = new HttpRequestException("offline");

        _clock.Advance(TimeSpan.FromMinutes(97)); // 80 % of the token: the silent re-launch wants to swap the frame
        player.WaitForAssertion(() => Assert.Equal("Failed", player.Find("[data-testid=package-save-state]").GetAttribute("data-status")), Wait);

        Assert.Single(_api.Launches); // no new token was asked for
        Assert.Equal(firstSrc, player.Find("[data-testid=package-frame]").GetAttribute("src")); // the frame stayed
        Assert.True(Bridge().HasPending); // and the unsaved progress with it
        _api.CommitFailure = null;
        player.Find("[data-testid=package-save-retry]").Click();
        player.WaitForAssertion(() => Assert.Equal(9, Assert.Single(_api.Commits).Request.Seq), Wait);
    }

    [Fact]
    public async Task Expired_token_offers_reload_with_the_same_attempt()
    {
        var player = await StartedAsync();
        _api.LaunchFailure = new HttpRequestException("offline"); // the silent re-launch at 80 % fails, so the frame runs until the token ends
        _clock.Advance(TimeSpan.FromMinutes(121));
        player.WaitForElement("[data-testid=package-expired]", Wait);
        Assert.Equal("alert", player.Find("[data-testid=package-expired]").GetAttribute("role"));

        _api.LaunchFailure = null;
        player.Find("[data-testid=package-reload]").Click();

        player.WaitForAssertion(() => Assert.Empty(player.FindAll("[data-testid=package-expired]")), Wait);
        Assert.Equal(_api.AttemptOf(PackageId), Bridge().AttemptId);
        Assert.Equal(2, _api.Launches.Count);
    }

    // ---- who may do what, and the states around the frame --------------------------------------------------------------------

    [Fact]
    public async Task Preview_is_staff_only_records_nothing_and_shows_the_banner()
    {
        var student = await RenderPlayerAsync();
        Assert.Empty(student.FindAll("[data-testid=package-preview]")); // a student never sees it
        await DisposeComponentsAsync();

        var staff = await RenderPlayerAsync(staff: true);
        Assert.Empty(staff.FindAll("[data-testid=package-start]")); // staff preview, they do not launch
        staff.Find("[data-testid=package-preview]").Click();
        staff.WaitForElement("[data-testid=package-frame]", Wait);
        await staff.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.suspend_data"] = "s" }));
        await staff.InvokeAsync(() => Bridge().FlushAsync());

        Assert.Contains("nothing is recorded", staff.Find("[data-testid=package-preview-banner]").TextContent);
        Assert.Single(_api.Previews);
        Assert.Empty(_api.Launches);
        Assert.Empty(_api.Commits);
        Assert.Empty(staff.FindAll("[data-testid=package-save-state]")); // no save state in a preview
    }

    [Fact]
    public async Task Hidden_module_launch_is_not_found()
    {
        _api.LaunchFailure = new ApiProblemException(404, "not_found", null, null);
        var player = await RenderPlayerAsync();

        player.Find("[data-testid=package-start]").Click();

        player.WaitForElement("[data-testid=package-not-available]", Wait);
        Assert.Empty(player.FindAll("[data-testid=package-frame]"));
        Assert.NotNull(player.Find("[data-testid=package-back]"));
    }

    [Fact]
    public async Task Resume_offered_when_an_attempt_is_in_progress()
    {
        var player = await RenderPlayerAsync(state: CompletionStates.InProgress);

        Assert.Equal("Resume", player.Find("[data-testid=package-start]").TextContent.Trim());
        Assert.Equal("In progress", player.Find("[data-testid=package-state-chip]").TextContent.Trim());
    }

    [Fact]
    public async Task Not_ready_and_host_not_configured_show_plain_states()
    {
        _api.LaunchFailure = new ApiProblemException(409, "package_not_ready", null, null);
        var player = await RenderPlayerAsync();
        player.Find("[data-testid=package-start]").Click();
        player.WaitForElement("[data-testid=package-not-ready]", Wait);
        await DisposeComponentsAsync();

        _api.LaunchFailure = new ApiProblemException(409, "package_host_not_configured", null, null);
        var other = await RenderPlayerAsync();
        other.Find("[data-testid=package-start]").Click();
        other.WaitForElement("[data-testid=package-host-not-configured]", Wait);
    }

    [Theory]
    [InlineData("en", "Full screen", "Exit")]
    [InlineData("ar", "ملء الشاشة", "خروج")]
    public async Task Fullscreen_button_has_a_label_in_both_languages_and_exit_stays_outside_the_frame(string culture, string fullscreen, string exit)
    {
        Culture.Current = new CultureInfo(culture);
        var player = await StartedAsync();

        Assert.Equal(fullscreen, player.Find("[data-testid=package-fullscreen]").GetAttribute("aria-label"));
        Assert.Equal(exit, player.Find("[data-testid=package-exit]").TextContent.Trim());
        Assert.Empty(player.FindAll(".lms-player-stage [data-testid=package-exit]")); // outside the stage that holds the frame
        Assert.Empty(player.FindAll(".lms-player-stage [data-testid=package-fullscreen]"));
        Assert.NotEmpty(player.FindAll(".lms-player [data-testid=package-exit]")); // inside the container, so full screen keeps it
    }

    [Fact]
    public async Task Exit_flushes_and_returns_to_the_start_card()
    {
        var player = await StartedAsync();
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 2, "Scorm", new() { ["cmi.suspend_data"] = "s" }));

        player.Find("[data-testid=package-exit]").Click();

        player.WaitForElement("[data-testid=package-start-card]", Wait);
        Assert.Equal(2, Assert.Single(_api.Commits).Request.Seq);
        Assert.Empty(player.FindAll("[data-testid=package-frame]"));
    }

    [Theory]
    [InlineData(ModuleKinds.Scorm)]
    [InlineData(ModuleKinds.H5p)]
    public async Task The_module_viewer_shows_the_player_for_scorm_and_h5p_modules(string kind)
    {
        await GrantAsync(Permissions.Packages.View, Permissions.Modules.View);
        Render<MudPopoverProvider>();
        var node = new ModuleNodeDto(Guid.NewGuid(), kind, 1, "نشاط", "Activity", ModuleVisibilities.Published, null, null, ModuleTargets.ClassCourse, null, null, null,
            CompletionRules.Submit, true, CompletionStates.NotStarted);
        var detail = new ModuleDetailDto(node, ClassCourse, Guid.NewGuid(), null, new ModuleContentDto(null, null, null, null, PackageId), null, null, false, DateTime.UtcNow);

        var body = Render<Lms.Client.Components.Content.Kinds.ModuleBody>(p => p.Add(x => x.Detail, detail));

        Assert.NotNull(body.Find("[data-testid=package-start-card]"));
        Assert.Empty(body.FindAll("[data-testid=module-unsupported]"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var player = await RenderPlayerAsync();

        Assert.Equal("ابدأ", player.Find("[data-testid=package-start]").TextContent.Trim());
        Assert.Contains("جهة أخرى", player.Find("[data-testid=package-third-party]").TextContent);
        Assert.Equal("لم يبدأ", player.Find("[data-testid=package-state-chip]").TextContent.Trim());
    }
}
