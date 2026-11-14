using Bunit;
using Lms.Client.Pages.Messaging;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Messaging;

/// <summary>M6 Task 24: report a message and block a person (#612 C7), against the messaging fake. The signed-in user is Sara, a student.</summary>
public sealed class ReportBlockTests : ClientTestContext
{
    private static FakeMessagingUser Sara => FakeMessagingApi.Sara;
    private static FakeMessagingUser Huda => FakeMessagingApi.Huda;

    private DateTime _clock = DateTime.UtcNow.AddHours(-2);
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudPopoverProvider> _popovers = default!;

    public ReportBlockTests() => MessagingApi.Now = () => _clock = _clock.AddSeconds(1);

    private async Task SignInAsync(FakeMessagingUser who)
    {
        Api.Me = Api.Me with { UserId = who.Id };
        await GrantAsync(Permissions.Messaging.View, Permissions.Messaging.Send);
        MessagingApi.Me = who;
    }

    private async Task<(ConversationDto Conversation, MessageDto FromHuda)> HudaWritesToSaraAsync()
    {
        MessagingApi.Me = Huda;
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Sara.Id], null, "please read"));
        var m = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();
        MessagingApi.Me = Sara;
        return (c, m);
    }

    private IRenderedComponent<MessagesPage> RenderThread(Guid id)
    {
        _dialogs = Render<MudDialogProvider>();
        _popovers = Render<MudPopoverProvider>();
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, id));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread]")));
        return cut;
    }

    private void PickReason() => _dialogs.Find($"[data-testid=report-reason-{ReportReasons.Safety}]").Click();

    private void OpenReport(IRenderedComponent<MessagesPage> cut, MessageDto m)
    {
        cut.Find($"[data-testid=msg-report-{m.Id}]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=report]")));
    }

    [Fact]
    public async Task Report_reasons_follow_the_contract_with_safety_first()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);

        OpenReport(cut, m);

        var reasons = _dialogs.FindAll("[data-testid^=report-reason-]").Select(e => e.GetAttribute("data-testid")!["report-reason-".Length..]).ToArray();
        Assert.Equal(ReportReasons.Safety, reasons[0]);
        Assert.Equal(ReportReasons.All.OrderBy(r => r), reasons.OrderBy(r => r));
    }

    [Fact]
    public async Task Report_limits_the_note_to_500()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);

        OpenReport(cut, m);

        Assert.Equal(MessagingLimits.ReportNoteMaxLength.ToString(), _dialogs.Find("[data-testid=report-note] textarea, textarea[data-testid=report-note]").GetAttribute("maxlength"));
    }

    [Fact]
    public async Task Report_marks_the_message_for_the_reporter()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);
        OpenReport(cut, m);

        PickReason();
        _dialogs.Find("[data-testid=report-submit]").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-reported-{m.Id}]")));
        Assert.Empty(cut.FindAll($"[data-testid=msg-report-{m.Id}]"));
        var (messageId, request) = Assert.Single(MessagingApi.Reports);
        Assert.Equal(m.Id, messageId);
        Assert.Equal(ReportReasons.Safety, request.Reason);
    }

    [Fact]
    public async Task Duplicate_report_counts_as_reported()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        await MessagingApi.ReportAsync(m.Id, new ReportMessageRequest(ReportReasons.Spam, null)); // reported earlier, the page does not know
        var cut = RenderThread(c.Id);
        OpenReport(cut, m);

        PickReason();
        _dialogs.Find("[data-testid=report-submit]").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=msg-reported-{m.Id}]")));
        Assert.Empty(_dialogs.FindAll("[data-testid=report]"));
    }

    [Fact]
    public async Task Submit_stays_off_until_a_reason_is_picked()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);
        OpenReport(cut, m);

        Assert.True(_dialogs.Find("[data-testid=report-submit]").HasAttribute("disabled"));
        PickReason();
        _dialogs.WaitForAssertion(() => Assert.False(_dialogs.Find("[data-testid=report-submit]").HasAttribute("disabled")));
    }

    [Fact]
    public async Task There_is_no_report_on_my_own_message()
    {
        await SignInAsync(Sara);
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Huda.Id], null, "mine"));
        var mine = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();

        var cut = RenderThread(c.Id);

        Assert.Empty(cut.FindAll($"[data-testid=msg-report-{mine.Id}]"));
    }

    [Fact]
    public async Task There_is_no_report_on_a_deleted_message()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        MessagingApi.Me = Huda;
        await MessagingApi.DeleteMessageAsync(c.Id, m.Id);
        MessagingApi.Me = Sara;

        var cut = RenderThread(c.Id);

        Assert.Empty(cut.FindAll($"[data-testid=msg-report-{m.Id}]"));
        Assert.Contains("lms-msg-removed", cut.Find($"[data-testid=msg-{m.Id}]").ClassName);
    }

    [Fact]
    public async Task There_is_no_block_menu_in_a_group()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, FakeMessagingApi.Omar.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, FakeMessagingApi.Omar.Id], "10-A", "Welcome"));
        await SignInAsync(Sara);

        var cut = RenderThread(g.Id);

        Assert.Empty(cut.FindAll("[data-testid=thread-more]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=thread-members]"));
    }

    [Fact]
    public async Task Report_rate_limited_keeps_the_note()
    {
        await SignInAsync(Sara);
        var (c, m) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);
        OpenReport(cut, m);
        _dialogs.Find("textarea").Input("they keep writing to me");
        MessagingApi.RateLimited = true;

        PickReason();
        _dialogs.Find("[data-testid=report-submit]").Click();

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=report-error]")));
        Assert.Equal("they keep writing to me", _dialogs.Find("textarea").GetAttribute("value"));
        Assert.Empty(MessagingApi.Reports);
    }

    [Fact]
    public async Task Block_hides_the_composer_and_lists_the_person()
    {
        await SignInAsync(Sara);
        var (c, _) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);

        cut.Find("[data-testid=thread-more] button, button[data-testid=thread-more]").Click();
        _popovers.WaitForAssertion(() => Assert.NotEmpty(_popovers.FindAll("[data-testid=thread-block]")));
        _popovers.Find("[data-testid=thread-block]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=block]")));
        _dialogs.Find("[data-testid=block-confirm]").Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=composer]")));
        Assert.NotEmpty(cut.FindAll("[data-testid=composer-off]"));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll($"[data-testid=blocked-{Huda.Id}]")));
    }

    [Fact]
    public async Task Unblock_restores_sending()
    {
        await SignInAsync(Sara);
        var (c, _) = await HudaWritesToSaraAsync();
        await MessagingApi.BlockAsync(Huda.Id);
        var cut = RenderThread(c.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=composer-unblock]")));

        cut.Find("[data-testid=composer-unblock]").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=composer]")));
        Assert.Empty(cut.FindAll("[data-testid=blocked-card]"));
        Assert.Empty(await MessagingApi.ListBlocksAsync());
    }

    [Fact]
    public async Task Blocked_by_other_is_never_shown_to_the_blocked_person()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, Sara.Id));
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Sara.Id], null, "hello"));
        MessagingApi.Me = Sara;
        await MessagingApi.BlockAsync(Huda.Id); // Sara blocks Huda
        MessagingApi.Me = Huda;

        var cut = RenderThread(c.Id);

        Assert.Empty(cut.FindAll("[data-testid=composer-unblock]")); // no Unblock: it is not their block
        Assert.Empty(cut.FindAll("[data-testid=blocked-card]"));
        var reason = cut.Find("[data-testid=composer-off]").TextContent; // L18: one generic sentence for a block and a rule
        Assert.Contains("لا يمكنك إرسال رسائل في هذه المحادثة", reason);
        Assert.DoesNotContain("حظر", cut.Find("[data-testid=thread]").TextContent);
    }

    [Fact]
    public async Task Dialogs_say_block_here_and_never_restrict()
    {
        Culture.Current = new System.Globalization.CultureInfo("en");
        await SignInAsync(Sara);
        var (c, _) = await HudaWritesToSaraAsync();
        var cut = RenderThread(c.Id);

        cut.Find("[data-testid=thread-more] button, button[data-testid=thread-more]").Click();
        _popovers.WaitForAssertion(() => Assert.NotEmpty(_popovers.FindAll("[data-testid=thread-block]")));
        _popovers.Find("[data-testid=thread-block]").Click();
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=block]")));

        var text = _dialogs.Find("[data-testid=block]").TextContent;
        Assert.Contains("Block", _dialogs.Markup);
        Assert.DoesNotContain("restrict", _dialogs.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("restrict", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unblocking_from_the_card_gives_the_open_thread_its_composer_back()
    {
        await SignInAsync(Sara);
        var (c, _) = await HudaWritesToSaraAsync();
        await MessagingApi.BlockAsync(Huda.Id);
        var cut = RenderThread(c.Id);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=composer-unblock]")));

        cut.Find($"[data-testid=unblock-{Huda.Id}]").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=composer]")));
        Assert.Empty(cut.FindAll("[data-testid=composer-off]"));
    }
}
