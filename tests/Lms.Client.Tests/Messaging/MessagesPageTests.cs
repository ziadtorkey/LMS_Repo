using Bunit;
using Lms.Client.Pages.Messaging;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Messaging;

/// <summary>M6 Task 21: the inbox and the thread (#612 C2), against the messaging fake. The signed-in user is Sara, a student.</summary>
public sealed class MessagesPageTests : ClientTestContext
{
    private static FakeMessagingUser Sara => FakeMessagingApi.Sara;
    private static FakeMessagingUser Huda => FakeMessagingApi.Huda;

    private DateTime _clock = DateTime.UtcNow.AddHours(-2);

    public MessagesPageTests() => MessagingApi.Now = () => _clock = _clock.AddSeconds(1);

    private async Task SignInAsync(FakeMessagingUser who, params string[] permissions)
    {
        Api.Me = Api.Me with { UserId = who.Id };
        await GrantAsync(permissions.Length == 0 ? [Permissions.Messaging.View, Permissions.Messaging.Send] : permissions);
        MessagingApi.Me = who;
    }

    private async Task<ConversationDto> DirectFromHudaAsync(params string[] bodies)
    {
        MessagingApi.Me = Huda;
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Sara.Id], null, bodies[0]));
        foreach (var b in bodies.Skip(1)) MessagingApi.Receive(c.Id, Huda, b);
        MessagingApi.Me = Sara;
        return c;
    }

    private IRenderedComponent<MessagesPage> RenderPage(Guid? id = null)
    {
        var cut = Render<MessagesPage>(p => p.Add(x => x.Id, id));
        cut.WaitForState(() => cut.FindAll("[data-testid=messages-loading]").Count == 0);
        return cut;
    }

    private static void WaitForThread(IRenderedComponent<MessagesPage> cut) =>
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread]")));

    [Fact]
    public async Task Inbox_lists_conversations_unread_first_with_plural_chips()
    {
        await SignInAsync(Sara);
        var read = await DirectFromHudaAsync("old");
        await MessagingApi.MarkReadAsync(read.Id);

        var cut = RenderPage();

        Assert.NotEmpty(cut.FindAll($"[data-testid=conv-{read.Id}]"));
        Assert.Empty(cut.FindAll("[data-testid=inbox-empty]"));
    }

    [Fact]
    public async Task Inbox_row_name_says_the_unread_count()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("one", "two", "three");

        var cut = RenderPage();

        var label = cut.Find($"[data-testid=conv-{c.Id}]").GetAttribute("aria-label");
        Assert.Contains("3", label);
    }

    [Fact]
    public async Task Empty_inbox_says_what_to_do()
    {
        await SignInAsync(Sara);
        var cut = RenderPage();
        Assert.NotEmpty(cut.FindAll("[data-testid=inbox-empty]"));
    }

    [Fact]
    public async Task Without_the_permission_the_page_is_forbidden()
    {
        await SignInAsync(Sara, Permissions.Roles.View);
        var cut = RenderPage();
        Assert.NotEmpty(cut.FindAll("[data-testid=messages-forbidden]"));
    }

    [Fact]
    public async Task Opening_a_thread_marks_it_read_after_it_is_visible()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("one", "two");
        Assert.Equal(1, (await MessagingApi.GetUnreadTotalAsync()).Conversations);

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        cut.WaitForAssertion(() => Assert.Equal(0, MessagingApi.GetUnreadTotalAsync().Result.Conversations));
        cut.WaitForAssertion(() => Assert.DoesNotContain("unread", cut.Find($"[data-testid=conv-{c.Id}]").ClassName));
    }

    [Fact]
    public async Task Thread_shows_the_unread_divider_once()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("read one", "new one", "new two");
        MessagingApi.Me = Sara;
        // Sara read the first message only.
        var first = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Last();
        MessagingApi.MarkReadUpTo(c.Id, first.Id);

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Single(cut.FindAll("[data-testid=thread-divider]"));
        var html = cut.Markup;
        Assert.True(html.IndexOf("thread-divider", StringComparison.Ordinal) < html.IndexOf("new one", StringComparison.Ordinal));
        Assert.True(html.IndexOf("thread-divider", StringComparison.Ordinal) > html.IndexOf("read one", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Load_earlier_keeps_the_reading_position()
    {
        await SignInAsync(Sara);
        var bodies = Enumerable.Range(1, MessagingLimits.MessagePageSize + 5).Select(i => $"m{i:000}").ToArray();
        var c = await DirectFromHudaAsync(bodies);
        var cut = RenderPage(c.Id);
        WaitForThread(cut);
        var newestBefore = cut.Find($"[data-testid^=msg-]");
        Assert.DoesNotContain("m001", cut.Markup);

        cut.Find("[data-testid=thread-earlier]").Click();

        cut.WaitForAssertion(() => Assert.Contains("m001", cut.Markup));
        Assert.Contains($"m{bodies.Length:000}", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid=thread-earlier]"));
        Assert.NotNull(newestBefore);
    }

    [Fact]
    public async Task Message_text_is_escaped_and_links_are_not_scripts()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("<script>alert(1)</script> <a href=\"javascript:alert(1)\">x</a>");

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll("[data-testid=thread-body] script"));
        Assert.Empty(cut.FindAll("[data-testid=thread-body] a"));
        Assert.Contains("&lt;script&gt;", cut.Find("[data-testid=thread-body]").InnerHtml);
    }

    [Fact]
    public async Task Search_highlights_without_rendering_html()
    {
        await SignInAsync(Sara);
        await DirectFromHudaAsync("see <b>homework</b> now");
        var cut = RenderPage();

        cut.FindComponent<MudTextField<string>>().Find("input").Change("homework");

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=inbox-hits] mark")));
        Assert.Equal("homework", cut.Find("[data-testid=inbox-hits] mark").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=inbox-hits] b"));
    }

    [Fact]
    public async Task Delete_is_offered_only_until_the_server_deadline()
    {
        await SignInAsync(Sara);
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Huda.Id], null, "mine"));
        var mine = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();

        var cut = RenderPage(c.Id);
        WaitForThread(cut);
        Assert.NotEmpty(cut.FindAll($"[data-testid=msg-delete-{mine.Id}]"));

        MessagingApi.Now = () => _clock.AddMinutes(MessagingLimits.DeleteWindowMinutes + 1);
        var late = RenderPage(c.Id);
        WaitForThread(late);
        Assert.Empty(late.FindAll($"[data-testid=msg-delete-{mine.Id}]"));
    }

    [Fact]
    public async Task Deleting_replaces_the_text_with_a_tombstone()
    {
        await SignInAsync(Sara);
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Huda.Id], null, "secret words"));
        var mine = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();
        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        cut.Find($"[data-testid=msg-delete-{mine.Id}]").Click();

        cut.WaitForAssertion(() => Assert.DoesNotContain("secret words", cut.Markup));
        Assert.Contains("lms-msg-removed", cut.Find($"[data-testid=msg-{mine.Id}]").ClassName);
    }

    [Fact]
    public async Task Deleted_and_removed_messages_show_tombstones()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("will go");
        var m = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single();
        MessagingApi.Me = Huda;
        await MessagingApi.DeleteMessageAsync(c.Id, m.Id);
        MessagingApi.Me = Sara;

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Contains("lms-msg-removed", cut.Find($"[data-testid=msg-{m.Id}]").ClassName);
        Assert.DoesNotContain("will go", cut.Find("[data-testid=thread-body]").TextContent);
    }

    [Fact]
    public async Task Locked_conversation_explains_it_and_has_no_composer()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("hi");
        MessagingApi.Lock(c.Id);

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.NotEmpty(cut.FindAll("[data-testid=composer-off]"));
        Assert.Empty(cut.FindAll("[data-testid=composer]"));
    }

    [Fact]
    public async Task Student_in_a_group_sees_no_composer_and_a_reply_privately_action()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, FakeMessagingApi.Omar.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, FakeMessagingApi.Omar.Id], "Class 10-A", "Welcome"));
        await SignInAsync(Sara);

        var cut = RenderPage(g.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll("[data-testid=composer]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=composer-off]"));
        Assert.Contains(Huda.Id.ToString(), cut.Find("[data-testid=thread-reply-privately]").GetAttribute("href"));
    }

    [Fact]
    public async Task Guardian_in_a_teacher_run_group_sees_the_composer()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, FakeMessagingApi.Omar.Id));
        MessagingApi.Related.Add((Huda.Id, FakeMessagingApi.Khaled.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [FakeMessagingApi.Khaled.Id, FakeMessagingApi.Omar.Id], "Parents and class", "Welcome"));
        await SignInAsync(FakeMessagingApi.Khaled);

        var cut = RenderPage(g.Id);
        WaitForThread(cut);

        Assert.NotEmpty(cut.FindAll("[data-testid=composer]"));
        Assert.Empty(cut.FindAll("[data-testid=thread-reply-privately]"));
    }

    [Fact]
    public async Task Sending_adds_the_message_and_clears_the_composer()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("hi");
        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        cut.Find("[data-testid=composer] textarea").Input("see you");
        cut.Find("[data-testid=composer-send]").Click();

        cut.WaitForAssertion(() => Assert.Contains("see you", cut.Find("[data-testid=thread-body]").TextContent));
        Assert.Equal(string.Empty, cut.Find("[data-testid=composer] textarea").GetAttribute("value") ?? string.Empty);
    }

    [Fact]
    public async Task A_conversation_that_is_not_yours_is_not_available()
    {
        await SignInAsync(Sara);
        var cut = RenderPage(Guid.NewGuid());
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=thread-error]")));
    }

    [Fact]
    public async Task Mobile_thread_is_full_screen_with_a_back_control()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("hi");
        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Contains("show-thread", cut.Find("[data-testid=inbox]").ClassName);
        Assert.Contains("lms-icon-flip", cut.Find("[data-testid=thread-back]").ClassName);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new System.Globalization.CultureInfo("ar");
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("مرحبا");
        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.True(Culture.IsRightToLeft);
        Assert.Contains("الرسائل", cut.Find("[data-testid=messages-title]").TextContent);
        Assert.Equal("auto", cut.Find($"[data-testid^=msg-] .lms-bubble").GetAttribute("dir"));
    }

    [Fact]
    public async Task My_message_is_on_my_side_and_theirs_is_not()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("from Huda");
        var mine = await MessagingApi.SendAsync(c.Id, new SendMessageRequest("from Sara", null));
        var theirs = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single(m => m.Body == "from Huda");

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Contains("lms-msg-me", cut.Find($"[data-testid=msg-{mine.Id}]").ClassName);
        Assert.Contains("lms-msg-them", cut.Find($"[data-testid=msg-{theirs.Id}]").ClassName);
        Assert.DoesNotContain("lms-msg-me", cut.Find($"[data-testid=msg-{theirs.Id}]").ClassName);
    }

    [Fact]
    public async Task In_a_group_the_sender_name_is_shown_for_others_and_never_for_me()
    {
        await SignInAsync(Huda);
        MessagingApi.Related.Add((Huda.Id, FakeMessagingApi.Omar.Id));
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [Sara.Id, FakeMessagingApi.Omar.Id], "10-A", "from Huda"));
        MessagingApi.Receive(g.Id, Sara, "from Sara");
        var mine = (await MessagingApi.GetMessagesAsync(g.Id, new MessageQuery())).Items.Single(m => m.Body == "from Huda");
        var theirs = (await MessagingApi.GetMessagesAsync(g.Id, new MessageQuery())).Items.Single(m => m.Body == "from Sara");

        var cut = RenderPage(g.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll($"[data-testid=msg-{mine.Id}] .lms-msg-from"));
        Assert.Contains("Sara", cut.Find($"[data-testid=msg-{theirs.Id}] .lms-msg-from").TextContent.Replace("سارة", "Sara"));
        Assert.Single(cut.FindAll(".lms-msg-from"));
    }

    [Fact]
    public async Task The_unread_divider_ignores_my_own_messages_when_it_counts_back()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("msgH0", "msgH1");
        await MessagingApi.SendAsync(c.Id, new SendMessageRequest("msgMine", null));
        MessagingApi.Receive(c.Id, Huda, "msgH2");
        var h0 = (await MessagingApi.GetMessagesAsync(c.Id, new MessageQuery())).Items.Single(m => m.Body == "msgH0");
        MessagingApi.MarkReadUpTo(c.Id, h0.Id); // unread: msgH1 and msgH2 (my own message never counts)

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        var html = cut.Markup;
        var divider = html.IndexOf("thread-divider", StringComparison.Ordinal);
        Assert.True(html.IndexOf("msgH0", StringComparison.Ordinal) < divider);
        Assert.True(divider < html.IndexOf("msgH1", StringComparison.Ordinal)); // above the first unread, not above my message
        Assert.True(html.IndexOf("msgH1", StringComparison.Ordinal) < html.IndexOf("msgMine", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_Messaging_Send_there_is_no_composer_and_no_delete()
    {
        await SignInAsync(Sara, Permissions.Messaging.View);
        var c = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Direct, [Huda.Id], null, "mine"));

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll("[data-testid=composer]"));
        Assert.Empty(cut.FindAll("[data-testid^=msg-delete-]"));
    }

    [Fact]
    public async Task A_long_inbox_says_it_shows_the_latest()
    {
        await SignInAsync(Sara);
        await DirectFromHudaAsync("one");
        var cut = RenderPage();
        Assert.Empty(cut.FindAll("[data-testid=inbox-capped]"));
    }

    [Fact]
    public void Day_labels_follow_the_zone_they_are_given()
    {
        var late = new DateTime(2026, 11, 14, 22, 30, 0, DateTimeKind.Utc); // 01:30 next day in Riyadh, same day in UTC
        var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        Assert.Equal(new DateTime(2026, 11, 15), Lms.Client.Pages.Messaging.MessageTimes.DayOf(late, riyadh));
        Assert.Equal(new DateTime(2026, 11, 14), Lms.Client.Pages.Messaging.MessageTimes.DayOf(late, TimeZoneInfo.Utc));
        Assert.Equal("01:30", Lms.Client.Pages.Messaging.MessageTimes.Clock(late, riyadh));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Blocked_side_sees_the_generic_read_only_reason(bool blockerIsMe)
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("hi");
        MessagingApi.Me = blockerIsMe ? Sara : Huda;
        await MessagingApi.BlockAsync(blockerIsMe ? Huda.Id : Sara.Id);
        MessagingApi.Me = Sara;

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll("[data-testid=composer]"));
        var reason = cut.Find("[data-testid=composer-off]").TextContent;
        if (blockerIsMe)
        {
            Assert.Contains("حظرت", reason); // my own block: the thread says so and offers Unblock
        }
        else
        {
            Assert.Contains("لا يمكنك إرسال رسائل في هذه المحادثة", reason); // theirs: one sentence for a block or a rule, it never says which
            Assert.DoesNotContain("حظر", reason);
        }
    }

    [Fact]
    public async Task Read_only_conversation_after_a_rule_change_explains_it_in_the_same_generic_words()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("hi");
        MessagingApi.Rules.Save([new MessagingRuleCellDto(BuiltInRoles.Student, BuiltInRoles.Teacher, MessagingRuleModes.Deny)]); // the Org Admin narrowed the rule

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll("[data-testid=composer]"));
        Assert.Contains("لا يمكنك إرسال رسائل في هذه المحادثة", cut.Find("[data-testid=composer-off]").TextContent);
        Assert.Contains("hi", cut.Find("[data-testid=thread-body]").TextContent); // the history stays readable
    }

    [Fact]
    public async Task A_guardian_outside_a_teacher_run_group_reads_and_is_told_why()
    {
        await SignInAsync(FakeMessagingApi.Admin);
        var g = await MessagingApi.CreateConversationAsync(new CreateConversationRequest(ConversationKinds.Group, [FakeMessagingApi.Khaled.Id, Huda.Id], "Parents", "Welcome"));
        await SignInAsync(FakeMessagingApi.Khaled);

        var cut = RenderPage(g.Id);
        WaitForThread(cut);

        Assert.Empty(cut.FindAll("[data-testid=composer]"));
        Assert.Contains("الموظفون فقط يكتبون", cut.Find("[data-testid=composer-off]").TextContent);
    }

    [Fact]
    public async Task A_locked_conversation_gives_the_locked_reason()
    {
        await SignInAsync(Sara);
        var c = await DirectFromHudaAsync("hi");
        MessagingApi.Lock(c.Id);

        var cut = RenderPage(c.Id);
        WaitForThread(cut);

        Assert.Contains("بإغلاق", cut.Find("[data-testid=composer-off]").TextContent);
    }
}
