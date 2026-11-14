using System.Globalization;
using Bunit;
using Lms.Client.Components.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>The shared year-end components (M7 H1): the polled job panel, the confirm-effect dialog and the year chip.</summary>
public sealed class YearEndComponentTests : ClientTestContext
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(5);

    private void English()
    {
        Culture.Current = new CultureInfo("en");
        FakeCultureService.Apply(new CultureInfo("en"));
    }

    private static Func<Task<JobSnapshot>> Script(params JobSnapshot[] steps)
    {
        var i = 0;
        return () => Task.FromResult(steps[Math.Min(i++, steps.Length - 1)]);
    }

    // --- JobProgressPanel

    [Fact]
    public void Job_panel_shows_progress_and_announces_politely_not_on_every_tick()
    {
        English();
        var polls = 0;
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, () => Task.FromResult(new JobSnapshot(Math.Min(++polls, 3), 100, false)))
            .Add(x => x.Interval, Fast).Add(x => x.AnnounceEvery, TimeSpan.FromMinutes(5)));

        cut.WaitForAssertion(() => Assert.Contains("Processed", cut.Find("[data-testid=job-text]").TextContent));
        cut.WaitForAssertion(() => Assert.True(polls >= 3));
        var announcer = cut.Find("[data-testid=job-announcer]");

        Assert.Equal("status", announcer.GetAttribute("role"));
        Assert.Equal("polite", announcer.GetAttribute("aria-live"));
        Assert.Equal("true", cut.Find("[data-testid=job-text]").GetAttribute("aria-hidden")); // the visible line is not a second live region
        Assert.Equal("Processed 1 of 100", announcer.TextContent); // the first read announces; later ticks inside the interval do not
    }

    [Fact]
    public void Job_panel_announces_again_after_ten_percent_and_the_interval()
    {
        English();
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, Script(new JobSnapshot(1, 100, false), new JobSnapshot(12, 100, false)))
            .Add(x => x.Interval, Fast).Add(x => x.AnnounceEvery, TimeSpan.Zero));

        cut.WaitForAssertion(() => Assert.Equal("Processed 12 of 100", cut.Find("[data-testid=job-announcer]").TextContent));
    }

    [Fact]
    public void Job_panel_keeps_the_result_after_the_job_ends_and_announces_the_end_once()
    {
        English();
        JobSnapshot? finished = null;
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, Script(new JobSnapshot(5, 10, false), new JobSnapshot(10, 10, true)))
            .Add(x => x.Interval, Fast)
            .Add(x => x.OnFinished, s => finished = s)
            .Add(x => x.Result, s => (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddMarkupContent(0, $"<p data-testid=\"done-card\">{s.Processed} of {s.Total}</p>"))));

        cut.WaitForElement("[data-testid=done-card]");

        Assert.Equal("10 of 10", cut.Find("[data-testid=done-card]").TextContent);
        Assert.Equal("Finished.", cut.Find("[data-testid=job-announcer]").TextContent);
        cut.WaitForAssertion(() => Assert.NotNull(finished)); // OnFinished follows the render of the result
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=done-card]"))); // still there after further renders
    }

    [Fact]
    public void Job_panel_says_when_the_job_failed()
    {
        English();
        var cut = Render<JobProgressPanel>(p => p.Add(x => x.Poll, Script(new JobSnapshot(2, 10, true, true))).Add(x => x.Interval, Fast));

        cut.WaitForAssertion(() => Assert.Contains("did not finish", cut.Find("[data-testid=job-announcer]").TextContent));
    }

    [Fact]
    public void A_failed_read_shows_a_note_and_the_next_tick_tries_again()
    {
        English();
        var calls = 0;
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, () => ++calls == 1 ? throw new HttpRequestException("offline") : Task.FromResult(new JobSnapshot(10, 10, true)))
            .Add(x => x.Interval, Fast));

        cut.WaitForAssertion(() => Assert.Equal("Finished.", cut.Find("[data-testid=job-announcer]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=job-poll-error]")); // gone once a read succeeds
        Assert.True(calls >= 2);
    }

    [Fact]
    public async Task Job_panel_stops_polling_when_it_is_removed_even_while_it_waits_for_the_next_tick()
    {
        English();
        var calls = 0;
        var cut = Render<JobProgressPanel>(p => p.Add(x => x.Poll, () => Task.FromResult(new JobSnapshot(++calls, 100, false))).Add(x => x.Interval, TimeSpan.FromMilliseconds(100)));
        cut.WaitForAssertion(() => Assert.Equal(1, calls));
        await Task.Delay(30); // the loop is now inside its wait for the next tick

        await cut.Instance.DisposeAsync();
        await Task.Delay(300); // three intervals

        Assert.Equal(1, calls); // a wait that was not cancelled would poll once more when it ends
    }

    [Fact]
    public void A_small_step_is_not_announced_but_a_ten_percent_step_is()
    {
        English();
        var polls = 0;
        var script = new[] { new JobSnapshot(1, 100, false), new JobSnapshot(5, 100, false), new JobSnapshot(10, 100, false), new JobSnapshot(11, 100, false), new JobSnapshot(12, 100, false) };
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, () => Task.FromResult(script[Math.Min(polls++, script.Length - 1)]))
            .Add(x => x.Interval, Fast).Add(x => x.AnnounceEvery, TimeSpan.Zero));

        cut.WaitForAssertion(() => Assert.True(polls >= 5));

        // 1 is announced (first); 5 and 10 are less than ten points on; 11 is exactly ten points on and is announced; 12 is one point past that
        Assert.Equal("Processed 11 of 100", cut.Find("[data-testid=job-announcer]").TextContent);
    }

    [Fact]
    public void A_big_step_inside_the_announce_interval_is_not_announced_either()
    {
        English();
        var polls = 0;
        var script = new[] { new JobSnapshot(1, 100, false), new JobSnapshot(60, 100, false), new JobSnapshot(80, 100, false) };
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, () => Task.FromResult(script[Math.Min(polls++, script.Length - 1)]))
            .Add(x => x.Interval, Fast).Add(x => x.AnnounceEvery, TimeSpan.FromMinutes(5)));

        cut.WaitForAssertion(() => Assert.True(polls >= 3));
        cut.WaitForAssertion(() => Assert.Equal("Processed 80 of 100", cut.Find("[data-testid=job-text]").TextContent));

        Assert.Equal("Processed 1 of 100", cut.Find("[data-testid=job-announcer]").TextContent); // the interval holds the announcement back
    }

    [Fact]
    public void The_end_is_announced_at_once_whatever_the_interval()
    {
        English();
        var polls = 0;
        var cut = Render<JobProgressPanel>(p => p
            .Add(x => x.Poll, () => Task.FromResult(polls++ == 0 ? new JobSnapshot(1, 100, false) : new JobSnapshot(100, 100, true)))
            .Add(x => x.Interval, Fast).Add(x => x.AnnounceEvery, TimeSpan.FromMinutes(5)));

        cut.WaitForAssertion(() => Assert.Equal("Finished.", cut.Find("[data-testid=job-announcer]").TextContent));
    }

    [Theory]
    [InlineData(0, 0, false, 0)]
    [InlineData(0, 0, true, 100)]
    [InlineData(5, 10, false, 50)]
    [InlineData(15, 10, false, 100)]
    public void Percent_is_clamped_and_handles_an_empty_job(int processed, int total, bool finished, int percent)
    {
        Assert.Equal(percent, new JobSnapshot(processed, total, finished).Percent);
    }

    // --- ConfirmEffectDialog

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, Task<bool> Answer)> OpenAsync(string? expected = null, string? count = null)
    {
        English();
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var answer = ConfirmEffectDialog.AskAsync(dialogs, "Close year", "Grades and attendance become read only.", count, expected, danger: true);
        provider.WaitForElement("[data-testid=confirm-effect]");
        await Task.CompletedTask;
        return (provider, answer);
    }

    [Fact]
    public async Task Confirm_effect_says_what_it_does_and_how_much_and_confirms()
    {
        var (provider, answer) = await OpenAsync(count: "120 students");

        Assert.Contains("read only", provider.Find("[data-testid=confirm-effect-text]").TextContent);
        Assert.Contains("120 students", provider.Find("[data-testid=confirm-effect-count]").TextContent);
        Assert.Empty(provider.FindAll("[data-testid=confirm-effect-input]")); // no typed confirmation asked for
        await provider.Find("[data-testid=confirm-effect-yes]").ClickAsync(new());
        Assert.True(await answer);
    }

    [Fact]
    public async Task A_typed_confirmation_enables_the_button_only_when_it_matches()
    {
        var (provider, answer) = await OpenAsync("2026/2027");
        Assert.True(provider.Find("[data-testid=confirm-effect-yes]").HasAttribute("disabled"));
        Assert.Contains("Type 2026/2027 to confirm", provider.Markup);

        provider.Find("input[data-testid=confirm-effect-input]").Input("2026");
        Assert.True(provider.Find("[data-testid=confirm-effect-yes]").HasAttribute("disabled"));
        provider.Find("input[data-testid=confirm-effect-input]").Input(" 2026/2027 ");
        provider.WaitForAssertion(() => Assert.False(provider.Find("[data-testid=confirm-effect-yes]").HasAttribute("disabled")));

        await provider.Find("[data-testid=confirm-effect-yes]").ClickAsync(new());
        Assert.True(await answer);
    }

    [Fact]
    public async Task Cancel_returns_false()
    {
        var (provider, answer) = await OpenAsync("x");

        await provider.Find("[data-testid=confirm-effect-cancel]").ClickAsync(new());

        Assert.False(await answer);
    }

    // --- ConfirmEffectDialog focus (explicit trap and return)

    [Fact]
    public async Task The_dialog_traps_focus_when_it_opens_and_releases_it_when_it_closes()
    {
        var module = JSInterop.SetupModule("./js/dialog-trap.js");
        module.Mode = JSRuntimeMode.Loose;
        var (provider, answer) = await OpenAsync();
        provider.WaitForAssertion(() => Assert.Single(module.Invocations["trap"]));
        Assert.Empty(module.Invocations["release"]); // still open

        await provider.Find("[data-testid=confirm-effect-cancel]").ClickAsync(new());
        Assert.False(await answer);

        provider.WaitForAssertion(() => Assert.Single(module.Invocations["release"]));
    }

    [Fact]
    public async Task Confirming_releases_the_focus_trap_too()
    {
        var module = JSInterop.SetupModule("./js/dialog-trap.js");
        module.Mode = JSRuntimeMode.Loose;
        var (provider, answer) = await OpenAsync();
        provider.WaitForAssertion(() => Assert.Single(module.Invocations["trap"]));

        await provider.Find("[data-testid=confirm-effect-yes]").ClickAsync(new());
        Assert.True(await answer);

        provider.WaitForAssertion(() => Assert.Single(module.Invocations["release"]));
    }

    [Fact]
    public void The_trap_script_moves_focus_in_wraps_tab_and_gives_focus_back_to_the_opener()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var js = File.ReadAllText(Path.Combine(dir!, "src", "Lms.Client", "wwwroot", "js", "dialog-trap.js"));

        Assert.Contains("document.activeElement", js); // the opener is remembered
        Assert.Contains("'Tab'", js);
        Assert.Contains("shiftKey", js); // Shift+Tab wraps backwards
        Assert.Contains("last.focus()", js);
        Assert.Contains("first.focus()", js);
        Assert.Contains("start.focus()", js); // focus moves into the dialog
        Assert.Contains("removeEventListener", js);
        Assert.Contains("target.focus()", js); // and goes back on release
    }

    // --- YearChip

    [Theory]
    [InlineData("Planned", false)]
    [InlineData("Active", false)]
    [InlineData("Closed", true)]
    public void Year_chip_shows_the_name_the_status_in_words_and_a_lock_only_when_closed(string status, bool locked)
    {
        English();
        var cut = Render<YearChip>(p => p.Add(x => x.NameAr, "٢٠٢٦").Add(x => x.NameEn, "2026/2027").Add(x => x.Status, status).Add(x => x.TestId, "chip"));

        Assert.Contains("2026/2027", cut.Find("[data-testid=chip]").TextContent);
        Assert.Contains(status, cut.Find("[data-testid=chip]").TextContent);
        Assert.Equal(status, cut.Find("[data-testid=chip]").GetAttribute("data-status"));
        Assert.Equal(locked, cut.FindAll("[data-testid=chip] svg").Count > 0);
    }

    [Fact]
    public void Year_chip_uses_the_arabic_name_and_status_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        FakeCultureService.Apply(new CultureInfo("ar"));

        var cut = Render<YearChip>(p => p.Add(x => x.NameAr, "٢٠٢٦").Add(x => x.NameEn, "2026").Add(x => x.Status, AcademicYearStatuses.Closed));

        Assert.Contains("٢٠٢٦", cut.Markup);
        Assert.DoesNotContain("Closed", cut.Find(".lms-year-chip").TextContent);
    }

    // --- resources

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Year_end_resources_exist_in_both_languages(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource>>();
        string[] keys = ["Job_Progress", "Job_Done", "Job_Failed", "Job_PollFailed", "Confirm_TypeToConfirm"];
        string[] codes = [.. IssueCodes()];

        Assert.All(keys, k => Assert.False(l[k].ResourceNotFound, k));
        Assert.All(codes, c => Assert.False(l[$"YearIssue_{c}"].ResourceNotFound, c));
    }

    private static IEnumerable<string> IssueCodes() => typeof(Lms.Shared.YearEnd.YearEndIssueCodes)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);
}
