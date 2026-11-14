using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lms.Client.Pages.Messaging;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Messaging;

/// <summary>
/// The FE1 pages of M6 (inbox, thread, compose, report, block) against the checklist of #615 §9 that a unit test can see: every control has an accessible name,
/// the thread is a labelled log, dialogs are named, the pages render in both languages, and the CSS added for them uses logical properties only (RTL).
/// </summary>
public sealed class M6Fe1AccessibilityTests : ClientTestContext
{
    private static FakeMessagingUser Sara => FakeMessagingApi.Sara;
    private static FakeMessagingUser Huda => FakeMessagingApi.Huda;

    private DateTime _clock = DateTime.UtcNow.AddHours(-2);
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;

    public M6Fe1AccessibilityTests() => MessagingApi.Now = () => _clock = _clock.AddSeconds(1);

    private async Task PrepareAsync(string culture, FakeMessagingUser who)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        Api.Me = Api.Me with { UserId = who.Id };
        await GrantAsync(Permissions.Messaging.View, Permissions.Messaging.Send);
        MessagingApi.Me = who;
        _dialogs = Render<MudDialogProvider>();
        _popovers = Render<MudPopoverProvider>();
    }

    /// <summary>The accessible name of a control; an aria-labelledby only counts when it points at an element that exists and has text.</summary>
    private static string Name(IElement e)
    {
        var labelledBy = e.GetAttribute("aria-labelledby");
        var fromReference = labelledBy is { Length: > 0 }
            ? string.Join(" ", labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(id => e.Owner?.GetElementById(id)?.TextContent.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)))
            : null;
        return new[] { e.GetAttribute("aria-label"), fromReference, e.GetAttribute("title"), e.TextContent.Trim(), e.QuerySelector("[aria-label]")?.GetAttribute("aria-label"), e.QuerySelector("img[alt]")?.GetAttribute("alt") }
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
    }

    /// <summary>The unnamed controls of a rendered tree: buttons and links with no name, inputs with no label, images with no alt.</summary>
    private static List<string> Problems(IRenderedComponent<IComponent> tree)
    {
        var problems = new List<string>();
        foreach (var b in tree.FindAll("button, a[href], [role=button], [role=menuitem]"))
        {
            if (string.IsNullOrWhiteSpace(Name(b))) problems.Add("no name: " + b.OuterHtml[..Math.Min(120, b.OuterHtml.Length)]);
        }

        foreach (var i in tree.FindAll("input:not([type=hidden]), textarea, select"))
        {
            var id = i.GetAttribute("id");
            var labelledBy = i.GetAttribute("aria-labelledby");
            var byReference = labelledBy is { Length: > 0 }
                && labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(r => tree.FindAll($"[id=\"{r}\"]").Any(t => !string.IsNullOrWhiteSpace(t.TextContent)));
            var labelled = !string.IsNullOrWhiteSpace(i.GetAttribute("aria-label")) || byReference
                || (id is { Length: > 0 } && tree.FindAll($"label[for=\"{id}\"]").Any(l => !string.IsNullOrWhiteSpace(l.TextContent)))
                || (i.Closest("label") is { } wrapping && !string.IsNullOrWhiteSpace(wrapping.TextContent));
            if (!labelled) problems.Add("no label: " + i.OuterHtml[..Math.Min(120, i.OuterHtml.Length)]);
        }

        foreach (var img in tree.FindAll("img:not([alt])")) problems.Add("no alt: " + img.OuterHtml);
        return problems;
    }

    private async Task<ConversationDto> HudaWritesToSaraAsync(params string[] bodies)
    {
        MessagingApi.Me = Huda;
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Sara.Id], null, bodies[0]));
        foreach (var b in bodies.Skip(1)) MessagingApi.Receive(c.Id, Huda, b);
        MessagingApi.Me = Sara;
        return c;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Inbox_and_thread_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Sara);
        var c = await HudaWritesToSaraAsync("first", "second", "third");
        await MessagingApi.ReportAsync((await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items[0].Id, new ReportMessageRequest(ReportReasons.Spam, null));
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, c.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread]")));

        Assert.Empty(Problems(cut));
        Assert.Equal("log", cut.Find("[data-testid=thread-body]").GetAttribute("role")); // the messages are a log, not a live region
        Assert.False(string.IsNullOrWhiteSpace(cut.Find("[data-testid=thread]").GetAttribute("aria-label"))); // and the pane has the conversation's name
        Assert.NotNull(cut.Find("[data-testid=thread-announce][role=status]")); // one polite status
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Inbox_rows_say_who_the_unread_count_and_the_preview(string culture)
    {
        await PrepareAsync(culture, Sara);
        var c = await HudaWritesToSaraAsync("one", "two");
        var cut = Render<MessagesPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=conv-{c.Id}]")));

        var label = cut.Find($"[data-testid=conv-{c.Id}]").GetAttribute("aria-label")!;
        Assert.Contains("2", label); // the unread state is never only bold text
        Assert.Contains("two", label);
        Assert.Empty(Problems(cut));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Compose_dialog_has_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Huda);
        MessagingApi.Related.Add((Huda.Id, FakeMessagingApi.Omar.Id));
        var cut = Render<MessagesPage>();
        cut.WaitForState(() => cut.FindAll("[data-testid=messages-new]").Count > 0);
        cut.Find("[data-testid=messages-new]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=picker-list]")));
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid^=picker-0]")));

        Assert.Empty(Problems(_dialogs));
        Assert.Equal("listbox", _dialogs.Find("[data-testid=picker-list]").GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(_dialogs.Find("[data-testid=picker-list]").GetAttribute("aria-label")));
        Assert.NotEmpty(_dialogs.FindAll("[data-testid=composer-send]")); // Send is always visible, Ctrl+Enter is only a shortcut
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Report_and_block_dialogs_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture, Sara);
        var c = await HudaWritesToSaraAsync("report me");
        var m = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, c.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-report-{m.Id}]")));

        cut.Find($"[data-testid=msg-report-{m.Id}]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=report]")));
        Assert.Empty(Problems(_dialogs));
        Assert.Equal("radiogroup", _dialogs.Find("[data-testid=report-reasons]").GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(_dialogs.Find("[data-testid=report-reasons]").GetAttribute("aria-label")));
        _dialogs.Find("[data-testid=report-cancel]").Click();

        cut.Find("[data-testid=thread-more] button, button[data-testid=thread-more]").Click();
        _popovers.WaitForAssertion(() => Assert.NotEmpty(_popovers.FindAll("[data-testid=thread-block]")));
        _popovers.Find("[data-testid=thread-block]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=block]")));
        Assert.Empty(Problems(_dialogs));
    }

    [Fact]
    public async Task The_reported_state_is_text_and_an_icon_never_colour_alone()
    {
        await PrepareAsync("en", Sara);
        var c = await HudaWritesToSaraAsync("flag me");
        var m = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, c.Id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-report-{m.Id}]")));
        cut.Find($"[data-testid=msg-report-{m.Id}]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=report-submit]")));
        _dialogs.Find("[data-testid=report-submit]").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-reported-{m.Id}]")));
        var chip = cut.Find($"[data-testid=msg-reported-{m.Id}]");
        Assert.Contains("Reported", chip.TextContent);
        Assert.NotEmpty(chip.QuerySelectorAll("svg"));
    }

    [Fact]
    public void The_css_of_the_FE1_pages_uses_logical_properties_only()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var css = File.ReadAllText(Path.Combine(dir!, "src", "Lms.Client", "wwwroot", "css", "app.css"));

        foreach (var marker in new[] { "/* ---------- Messages page (M6 Task 21)", "/* ---------- Compose and group members (M6 Task 22)" })
        {
            var start = css.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, marker);
            var next = css.IndexOf("/* ----------", start + marker.Length, StringComparison.Ordinal);
            var block = next < 0 ? css[start..] : css[start..next];
            foreach (var physical in new[] { "margin-left", "margin-right", "padding-left", "padding-right", "border-left", "border-right", " left:", " right:", "text-align: left", "text-align: right" })
            {
                Assert.DoesNotContain(physical, block);
            }
        }
    }
}
