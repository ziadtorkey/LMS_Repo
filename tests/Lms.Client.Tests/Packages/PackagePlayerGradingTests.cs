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

/// <summary>Opening a completed package again by the module's grading method (D57, design spec §5.1 and §5.3).</summary>
public sealed class PackagePlayerGradingTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid PackageId = Guid.NewGuid();
    private static readonly Guid ClassCourse = Guid.NewGuid();

    private readonly FakePackagePlayerApi _api = new();
    private readonly ManualClock _clock = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public PackagePlayerGradingTests()
    {
        _api.ServerNow = () => _clock.GetUtcNow().UtcDateTime;
        Services.AddSingleton<IPackagePlayerApi>(_api);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddSingleton(new PackageBridgeOptions { MinCommitInterval = TimeSpan.Zero, AutoRetries = 0 });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Culture.Current = new CultureInfo("en");
    }

    private static PackageAttemptDto Attempt(decimal score) => new(Guid.NewGuid(), "Completed", true, score, 0, 100, 5, DateTime.UtcNow);

    /// <summary>A student whose package has a completed attempt (graded score 85, latest attempt <paramref name="latest"/>).</summary>
    private void Completed(string method, decimal graded = 85, decimal latest = 85) =>
        _api.MyState = new PackageStudentStateDto(method, Attempt(latest), true, graded);

    private async Task<IRenderedComponent<PackagePlayer>> RenderAsync(string? state = CompletionStates.Completed, bool staff = false)
    {
        await GrantAsync(staff ? [Permissions.Packages.View, Permissions.Modules.Manage] : [Permissions.Packages.View, Permissions.Modules.View]);
        await Services.GetRequiredService<PermissionState>().RefreshAsync(); // the app loads permissions before any page
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        return Render<PackagePlayer>(p => p.Add(x => x.PackageId, PackageId).Add(x => x.ClassCourseId, ClassCourse).Add(x => x.Title, "Fractions lab").Add(x => x.State, state));
    }

    private PackageBridge Bridge() =>
        ((DotNetObjectReference<PackageBridge>)JSInterop.Invocations["lmsPackageBridge.attach"].Last().Arguments[1]!).Value;

    [Fact]
    public async Task Highest_offers_Try_again_without_a_confirm_and_launches_at_once()
    {
        Completed(PackageGradingMethods.Highest);
        var player = await RenderAsync();

        var button = player.Find("[data-testid=package-reopen-button]");
        Assert.Equal("Try again", button.TextContent.Trim());
        Assert.Contains("won't go down", player.Find("[data-testid=package-reopen-caption]").TextContent);
        Assert.Empty(player.FindAll("[data-testid=package-start]")); // Start gives way to the re-open action
        button.Click();

        player.WaitForElement("[data-testid=package-frame]", Wait);
        Assert.Single(_api.Launches);
        Assert.Empty(player.FindAll("[data-testid=package-practice-chip]"));
    }

    [Fact]
    public async Task Last_asks_first_and_quotes_the_graded_score()
    {
        Completed(PackageGradingMethods.Last, graded: 85, latest: 85);
        var player = await RenderAsync();
        Assert.Equal("Try again", player.Find("[data-testid=package-reopen-button]").TextContent.Trim());
        Assert.Contains("replaces this one", player.Find("[data-testid=package-reopen-caption]").TextContent);

        player.Find("[data-testid=package-reopen-button]").Click();

        _dialogs.WaitForElement("[data-testid=confirm-yes]", Wait);
        Assert.Contains("85 / 100", _dialogs.Markup);
        Assert.Contains("even if it's lower", _dialogs.Markup);
        Assert.Empty(_api.Launches); // nothing launched before the person agrees

        _dialogs.Find("[data-testid=confirm-no]").Click();
        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=confirm-yes]")), Wait);
        Assert.Empty(_api.Launches);

        player.Find("[data-testid=package-reopen-button]").Click();
        _dialogs.WaitForElement("[data-testid=confirm-yes]", Wait).Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);
        Assert.Single(_api.Launches);
    }

    [Fact]
    public async Task First_offers_a_review_that_is_not_graded_and_marks_the_attempt_as_practice()
    {
        Completed(PackageGradingMethods.First);
        var player = await RenderAsync();

        Assert.Equal("Review (not graded)", player.Find("[data-testid=package-reopen-button]").TextContent.Trim());
        Assert.Contains("practice", player.Find("[data-testid=package-reopen-caption]").TextContent);
        player.Find("[data-testid=package-reopen-button]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);

        Assert.Equal("Practice", player.Find("[data-testid=package-practice-chip]").TextContent.Trim());

        // The practice attempt ends: the card says it did not count and the graded score is still the first one.
        await player.InvokeAsync(() => Bridge().OnPortMessage("finish", 1, "Scorm", new()));
        player.WaitForElement("[data-testid=package-result-card]", Wait);
        Assert.Contains("Practice finished", player.Find("[data-testid=package-practice-finished]").TextContent);
        Assert.Contains("85 / 100", player.Find("[data-testid=package-practice-finished]").TextContent);
    }

    [Fact]
    public async Task The_graded_score_is_shown_with_the_latest_attempt_when_it_differs()
    {
        Completed(PackageGradingMethods.Highest, graded: 85, latest: 70);
        var player = await RenderAsync();

        Assert.Equal("Score 85 / 100", player.Find("[data-testid=package-start-score]").TextContent.Trim());
        Assert.Equal("Latest attempt: 70 / 100", player.Find("[data-testid=package-latest-score]").TextContent.Trim());
    }

    [Fact]
    public async Task No_latest_attempt_line_when_it_is_the_graded_one()
    {
        Completed(PackageGradingMethods.Highest, graded: 85, latest: 85);
        var player = await RenderAsync();

        Assert.Equal("Score 85 / 100", player.Find("[data-testid=package-start-score]").TextContent.Trim());
        Assert.Empty(player.FindAll("[data-testid=package-latest-score]"));
    }

    [Fact]
    public async Task The_result_card_shows_the_graded_score_and_the_reopen_action_for_the_method()
    {
        var player = await RenderAsync(state: CompletionStates.NotStarted); // nothing completed yet: Start
        _api.MyState = new PackageStudentStateDto(PackageGradingMethods.Highest, Attempt(60), true, 90); // what the server holds after the attempt
        _api.FinishResult = Attempt(60);
        player.Find("[data-testid=package-start]").Click();
        player.WaitForElement("[data-testid=package-frame]", Wait);

        await player.InvokeAsync(() => Bridge().OnPortMessage("finish", 1, "Scorm", new()));

        player.WaitForElement("[data-testid=package-result-card]", Wait);
        Assert.Equal("Score 90 / 100", player.Find("[data-testid=package-result-score]").TextContent.Trim()); // the gradebook's, not the attempt's
        Assert.Equal("Latest attempt: 60 / 100", player.Find("[data-testid=package-latest-score]").TextContent.Trim());
        Assert.NotNull(player.Find("[data-testid=package-reopen-button]"));
    }

    [Fact]
    public async Task No_reopen_action_before_a_completed_attempt_or_while_one_is_in_progress()
    {
        var fresh = await RenderAsync(state: CompletionStates.NotStarted);
        Assert.Empty(fresh.FindAll("[data-testid=package-reopen]"));
        Assert.NotNull(fresh.Find("[data-testid=package-start]"));
        await DisposeComponentsAsync();

        Completed(PackageGradingMethods.Highest);
        var inProgress = await RenderAsync(state: CompletionStates.InProgress);
        Assert.Empty(inProgress.FindAll("[data-testid=package-reopen]"));
        Assert.Equal("Resume", inProgress.Find("[data-testid=package-start]").TextContent.Trim());
    }

    [Fact]
    public async Task If_my_state_cannot_be_read_the_card_falls_back_to_Start()
    {
        _api.MyStateFailure = new HttpRequestException("offline");
        var player = await RenderAsync(state: CompletionStates.Completed);

        Assert.NotNull(player.Find("[data-testid=package-start]"));
        Assert.Empty(player.FindAll("[data-testid=package-reopen]"));
    }

    [Fact]
    public async Task Staff_never_read_my_state_and_never_see_a_reopen_action()
    {
        Completed(PackageGradingMethods.Highest);
        var player = await RenderAsync(staff: true);

        Assert.Equal(0, _api.MyStateReads);
        Assert.Empty(player.FindAll("[data-testid=package-reopen]"));
        Assert.NotNull(player.Find("[data-testid=package-preview]"));
    }

    [Theory]
    [InlineData(PackageGradingMethods.Highest, "حاول مرة أخرى")]
    [InlineData(PackageGradingMethods.Last, "حاول مرة أخرى")]
    [InlineData(PackageGradingMethods.First, "مراجعة (غير محتسبة)")]
    public async Task The_reopen_wording_is_in_arabic(string method, string expected)
    {
        Culture.Current = new CultureInfo("ar");
        Completed(method);
        var player = await RenderAsync();

        Assert.Equal(expected, player.Find("[data-testid=package-reopen-button]").TextContent.Trim());
        Assert.Contains("الدرجة 85 / 100", player.Find("[data-testid=package-start-score]").TextContent);
    }
}
