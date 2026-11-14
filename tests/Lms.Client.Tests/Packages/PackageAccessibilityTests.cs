using System.Globalization;
using Bunit;
using Lms.Client.Components.Packages;
using Lms.Client.Services;
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

/// <summary>The package half of the M4b hardening (T19, X-10): keyboard and focus, polite announcements, the 360 px sweep in ar and en, stable test ids.</summary>
public sealed class PackageAccessibilityTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid PackageId = Guid.NewGuid();
    private static readonly Guid ClassCourse = Guid.NewGuid();

    private readonly FakePackagePlayerApi _api = new();
    private readonly ManualClock _clock = new();

    public PackageAccessibilityTests()
    {
        _api.ServerNow = () => _clock.GetUtcNow().UtcDateTime;
        Services.AddSingleton<IPackagePlayerApi>(_api);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton(new PackageBridgeOptions { MinCommitInterval = TimeSpan.Zero, AutoRetries = 0 });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Culture.Current = new CultureInfo("en");
    }

    private async Task<IRenderedComponent<PackagePlayer>> RenderAsync(bool staff = false, string? state = null)
    {
        await GrantAsync(staff ? [Permissions.Packages.View, Permissions.Modules.Manage] : [Permissions.Packages.View, Permissions.Modules.View]);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        return Render<PackagePlayer>(p => p.Add(x => x.PackageId, PackageId).Add(x => x.ClassCourseId, ClassCourse).Add(x => x.Title, "Fractions lab").Add(x => x.State, state));
    }

    private async Task<IRenderedComponent<PackagePlayer>> StartedAsync()
    {
        var player = await RenderAsync();
        player.Find("[data-testid=package-start]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);
        return player;
    }

    private PackageBridge Bridge() =>
        ((DotNetObjectReference<PackageBridge>)JSInterop.Invocations["lmsPackageBridge.attach"].Last().Arguments[1]!).Value;

    private int Focuses() => JSInterop.Invocations.Count(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

    private static string Announced(IRenderedComponent<PackagePlayer> player) => player.Find("[data-testid=package-announce]").TextContent.Trim();

    // ---- keyboard and focus ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Start_moves_keyboard_focus_to_the_player_container()
    {
        var player = await RenderAsync();
        var before = Focuses();

        player.Find("[data-testid=package-start]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);

        player.WaitForAssertion(() => Assert.True(Focuses() > before), Wait);
        Assert.Equal("-1", player.Find("[data-testid=package-player]").GetAttribute("tabindex")); // focusable by script, not a tab stop
    }

    [Fact]
    public async Task Resume_does_the_same_as_Start()
    {
        var player = await RenderAsync(state: CompletionStates.InProgress);
        Assert.Equal("Resume", player.Find("[data-testid=package-start]").TextContent.Trim()); // one id for both, the label differs
        var before = Focuses();

        player.Find("[data-testid=package-start]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);

        player.WaitForAssertion(() => Assert.True(Focuses() > before), Wait);
    }

    [Fact]
    public async Task Exit_puts_focus_back_on_the_start_card_and_says_so()
    {
        var player = await StartedAsync();
        var before = Focuses();

        player.Find("[data-testid=package-exit]").Click();
        player.WaitForElement("[data-testid=package-start-card]", Wait);

        player.WaitForAssertion(() => Assert.True(Focuses() > before), Wait);
        Assert.Equal("-1", player.Find("[data-testid=package-start-host]").GetAttribute("tabindex"));
        player.WaitForAssertion(() => Assert.Contains("start of the activity", Announced(player)), Wait);
    }

    [Fact]
    public async Task Expiry_focuses_the_alert_makes_the_frame_inert_and_Reload_returns_focus_to_the_player()
    {
        var player = await StartedAsync();
        _api.LaunchFailure = new HttpRequestException("offline"); // the silent re-launch at 80 % fails, so the token runs out
        _clock.Advance(TimeSpan.FromMinutes(121));
        player.WaitForElement("[data-testid=package-expired]", Wait);

        Assert.Equal("alert", player.Find("[data-testid=package-expired]").GetAttribute("role"));
        Assert.Equal("-1", player.Find("[data-testid=package-expired]").GetAttribute("tabindex"));
        Assert.True(player.Find("[data-testid=package-frame]").HasAttribute("inert")); // keyboard and pointer cannot reach the dead frame
        Assert.DoesNotContain("timed out", Announced(player)); // the focused alert is the one announcement

        _api.LaunchFailure = null;
        var before = Focuses();
        player.Find("[data-testid=package-reload]").Click();

        player.WaitForAssertion(() => Assert.Empty(player.FindAll("[data-testid=package-expired]")), Wait);
        player.WaitForAssertion(() => Assert.True(Focuses() > before), Wait);
        Assert.False(player.Find("[data-testid=package-frame]").HasAttribute("inert"));
    }

    [Fact]
    public async Task The_toolbar_is_reachable_before_the_frame_in_tab_order()
    {
        var player = await StartedAsync();

        var markup = player.Markup;

        Assert.True(markup.IndexOf("package-toolbar", StringComparison.Ordinal) < markup.IndexOf("package-frame", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("package-exit", StringComparison.Ordinal) < markup.IndexOf("package-frame", StringComparison.Ordinal));
    }

    // ---- polite announcements ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_player_has_one_polite_status_region_for_opened_preview_save_failure_expiry_and_finish()
    {
        var player = await StartedAsync();
        var region = player.Find("[data-testid=package-announce]");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Contains("lms-visually-hidden", region.ClassList);
        Assert.Contains("opened", Announced(player));

        _api.CommitFailure = new HttpRequestException("offline");
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.suspend_data"] = "s" }));
        await player.InvokeAsync(() => Bridge().FlushAsync());
        player.WaitForAssertion(() => Assert.Equal("Failed", player.Find("[data-testid=package-save-state]").GetAttribute("data-status")), Wait);
        Assert.DoesNotContain("Couldn't save", Announced(player)); // the save state announces it

        _api.CommitFailure = null;
        await player.InvokeAsync(() => Bridge().OnPortMessage("finish", 2, "Scorm", new()));
        player.WaitForElement("[data-testid=package-result-card]", Wait);
        player.WaitForAssertion(() => Assert.Contains("finished", Announced(player)), Wait);
    }

    /// <summary>How many live regions (status, alert, aria-live) carry this text right now.</summary>
    private static int LiveCopies(IRenderedComponent<PackagePlayer> player, string text) =>
        player.FindAll("[role=status], [role=alert], [aria-live]").Count(e => e.TextContent.Contains(text, StringComparison.Ordinal));

    [Fact]
    public async Task A_failed_save_and_an_expired_token_are_each_announced_exactly_once()
    {
        var player = await StartedAsync();
        _api.CommitFailure = new HttpRequestException("offline");
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.suspend_data"] = "s" }));
        await player.InvokeAsync(() => Bridge().FlushAsync());
        player.WaitForAssertion(() => Assert.Equal("Failed", player.Find("[data-testid=package-save-state]").GetAttribute("data-status")), Wait);

        Assert.Equal(1, LiveCopies(player, "Couldn't save"));

        _api.CommitFailure = null;
        _api.LaunchFailure = new HttpRequestException("offline");
        _clock.Advance(TimeSpan.FromMinutes(121));
        player.WaitForElement("[data-testid=package-expired]", Wait);

        Assert.Equal(1, LiveCopies(player, "timed out"));
    }

    [Fact]
    public async Task Reload_says_the_activity_opened_again()
    {
        var player = await StartedAsync();
        _api.LaunchFailure = new HttpRequestException("offline");
        _clock.Advance(TimeSpan.FromMinutes(121));
        player.WaitForElement("[data-testid=package-expired]", Wait);
        _api.LaunchFailure = null;

        player.Find("[data-testid=package-reload]").Click();

        player.WaitForAssertion(() => Assert.Empty(player.FindAll("[data-testid=package-expired]")), Wait);
        Assert.Contains("opened", Announced(player));
    }

    [Fact]
    public async Task The_same_message_twice_in_a_row_changes_the_region_so_it_is_read_again()
    {
        var player = await StartedAsync();
        var first = player.Find("[data-testid=package-announce]").TextContent;
        _api.LaunchFailure = new HttpRequestException("offline");
        _clock.Advance(TimeSpan.FromMinutes(121));
        player.WaitForElement("[data-testid=package-expired]", Wait);
        _api.LaunchFailure = null;

        player.Find("[data-testid=package-reload]").Click();

        player.WaitForAssertion(() => Assert.Empty(player.FindAll("[data-testid=package-expired]")), Wait);
        var second = player.Find("[data-testid=package-announce]").TextContent;
        Assert.Equal(first.Trim(' '), second.Trim(' ')); // the same words ...
        Assert.NotEqual(first, second); // ... in different text, so assistive technology announces them again
    }

    [Fact]
    public async Task A_preview_announces_that_nothing_is_recorded()
    {
        var player = await RenderAsync(staff: true);

        player.Find("[data-testid=package-preview]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);

        Assert.Contains("nothing is recorded", Announced(player));
        Assert.Contains("nothing is recorded", player.Find("[data-testid=package-preview-banner]").TextContent);
    }

    [Fact]
    public async Task The_save_state_is_a_polite_status_and_never_colour_alone()
    {
        var player = await StartedAsync();
        _api.CommitFailure = new HttpRequestException("offline");
        await player.InvokeAsync(() => Bridge().OnPortMessage("commit", 1, "Scorm", new() { ["cmi.suspend_data"] = "s" }));
        await player.InvokeAsync(() => Bridge().FlushAsync());

        player.WaitForAssertion(() => Assert.Equal("Failed", player.Find("[data-testid=package-save-state]").GetAttribute("data-status")), Wait);
        var state = player.Find("[data-testid=package-save-state]");

        Assert.Equal("status", state.GetAttribute("role"));
        Assert.Equal("polite", state.GetAttribute("aria-live"));
        Assert.Contains("Couldn't save", state.TextContent); // words and an icon, not only the red
        Assert.NotNull(state.QuerySelector("svg"));
    }

    // ---- the 360 px sweep, ar and en ---------------------------------------------------------------------------------------------

    private static void AssertNamed(IRenderedComponent<PackagePlayer> player, string where)
    {
        foreach (var control in player.FindAll("button, a[href], [role=button]"))
        {
            var name = (control.GetAttribute("aria-label") ?? control.GetAttribute("title") ?? control.TextContent).Trim();
            Assert.False(string.IsNullOrWhiteSpace(name), $"{where}: a control has no accessible name: {control.OuterHtml[..Math.Min(120, control.OuterHtml.Length)]}");
        }
        foreach (var frame in player.FindAll("iframe"))
            Assert.False(string.IsNullOrWhiteSpace(frame.GetAttribute("title")), $"{where}: an iframe has no title");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Every_state_has_named_controls_a_titled_frame_and_both_languages_resolve(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var player = await StartedAsync();
        AssertNamed(player, "running");
        Assert.DoesNotContain("Package_", player.Markup); // no unresolved resource key shows on screen

        _api.LaunchFailure = new HttpRequestException("offline");
        _clock.Advance(TimeSpan.FromMinutes(121));
        player.WaitForElement("[data-testid=package-expired]", Wait);
        AssertNamed(player, "expired");
        Assert.DoesNotContain("Package_", player.Markup);
        _api.LaunchFailure = null;

        player.Find("[data-testid=package-reload]").Click();
        player.WaitForAssertion(() => Assert.Empty(player.FindAll("[data-testid=package-expired]")), Wait);
        await player.InvokeAsync(() => Bridge().OnPortMessage("finish", 1, "Scorm", new()));
        player.WaitForElement("[data-testid=package-result-card]", Wait);
        AssertNamed(player, "result");
        Assert.DoesNotContain("Package_", player.Markup);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task The_start_card_is_named_and_carries_the_phone_hint(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var player = await RenderAsync();

        AssertNamed(player, "start card");
        Assert.NotNull(player.Find("[data-testid=package-third-party]"));
        Assert.DoesNotContain("Package_", player.Markup);
    }

    [Fact]
    public void The_stylesheet_has_the_phone_rules_for_the_player_toolbar_and_the_overlay()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Lms.Client", "wwwroot", "css", "lms-activities.css"))) dir = dir.Parent;
        var css = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Lms.Client", "wwwroot", "css", "lms-activities.css"));

        Assert.Contains("@media (max-width: 599.98px)", css);
        Assert.Contains(".lms-player-title { display: none; }", css); // the title gives way to the controls on a phone
        Assert.Contains(".lms-player-frame { display: block; inline-size: 100%; min-block-size: 70vh;", css); // full width, tall enough
        Assert.DoesNotContain("margin-left", css.Split("Package player")[1].Split("Save state")[0]); // logical properties only
    }

    // ---- ids that QA2's page objects (T23, #577) rely on ----------------------------------------------------------------------------

    [Fact]
    public async Task The_test_ids_of_the_page_objects_stay_stable()
    {
        var start = await RenderAsync();
        foreach (var id in new[] { "package-start-card", "package-start-title", "package-state-chip", "package-third-party", "package-phone-hint", "package-start" })
            Assert.NotEmpty(start.FindAll($"[data-testid={id}]"));
        start.Find("[data-testid=package-start]").Click();
        start.WaitForElement("[data-testid=package-frame]", Wait);
        foreach (var id in new[] { "package-player", "package-frame", "package-toolbar", "package-toolbar-title", "package-fullscreen", "package-exit", "package-save-state", "package-announce" })
            Assert.NotEmpty(start.FindAll($"[data-testid={id}]"));
        Assert.Equal("allow-scripts", start.Find("[data-testid=package-frame]").GetAttribute("sandbox"));
    }
}
