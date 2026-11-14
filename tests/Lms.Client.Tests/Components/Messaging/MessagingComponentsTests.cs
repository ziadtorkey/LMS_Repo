using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Lms.Client.Tests.Components.Messaging;

/// <summary>The M6 presentational components (spec #615 §2, §4, §8, §9): structure, words and accessibility, no data access.</summary>
public sealed class MessagingComponentsTests : ClientTestContext
{
    private static void English() => FakeCultureService.Apply(new CultureInfo("en"));

    [Fact]
    public void Conversation_row_is_one_link_whose_name_carries_the_unread_state()
    {
        English();
        var cut = Render<ConversationRow>(p => p
            .Add(c => c.Href, "messages/1").Add(c => c.Name, "Huda Al-Harbi").Add(c => c.Preview, "See you tomorrow")
            .Add(c => c.TimeText, "10:42").Add(c => c.UnreadCount, 2));

        var link = cut.Find("a.lms-conv");
        Assert.Equal("messages/1", link.GetAttribute("href"));
        Assert.Contains("unread", link.ClassList);
        Assert.Equal("Huda Al-Harbi, 2 unread, 10:42, See you tomorrow", link.GetAttribute("aria-label"));
        Assert.Null(link.GetAttribute("aria-current"));
        Assert.Equal("auto", cut.Find(".lms-conv-preview").GetAttribute("dir"));
        Assert.Equal("ltr", cut.Find(".lms-conv-time").GetAttribute("dir"));
    }

    [Fact]
    public void Current_read_conversation_has_aria_current_and_no_unread_badge()
    {
        English();
        var cut = Render<ConversationRow>(p => p
            .Add(c => c.Href, "messages/2").Add(c => c.Name, "Physics 1 · 10-A").Add(c => c.Preview, "Thanks")
            .Add(c => c.PreviewAuthor, "Sara").Add(c => c.TimeText, "Yesterday").Add(c => c.IsGroup, true).Add(c => c.Current, true));

        var link = cut.Find("a.lms-conv");
        Assert.Equal("true", link.GetAttribute("aria-current"));
        Assert.DoesNotContain("unread", link.ClassList);
        Assert.Empty(cut.FindAll(".lms-conv-count"));
        Assert.Contains("Sara: Thanks", cut.Find(".lms-conv-preview").TextContent);
    }

    [Fact]
    public void Own_and_other_messages_sit_on_opposite_sides_with_auto_direction_text()
    {
        English();
        var mine = Render<MessageBubble>(p => p.Add(c => c.Mine, true).Add(c => c.Text, "Hello").Add(c => c.TimeText, "09:40")
            .Add(c => c.Status, MessageStatus.Sent));
        var theirs = Render<MessageBubble>(p => p.Add(c => c.Text, "مرحبا").Add(c => c.From, "Omar").Add(c => c.TimeText, "09:41"));

        Assert.Contains("lms-msg-me", mine.Find(".lms-msg").ClassList);
        Assert.Contains("Sent", mine.Find(".lms-msg-meta").TextContent);
        Assert.Contains("lms-msg-them", theirs.Find(".lms-msg").ClassList);
        Assert.Equal("auto", theirs.Find(".lms-bubble").GetAttribute("dir"));
        Assert.Equal("Omar", theirs.Find(".lms-msg-from bdi").TextContent);
    }

    [Theory]
    [InlineData(MessageRemoval.ByModerator, false, "This message was removed by a moderator.")]
    [InlineData(MessageRemoval.BySender, false, "Message deleted")]
    [InlineData(MessageRemoval.BySender, true, "You deleted this message.")]
    public void Removed_messages_hide_the_text_and_say_who_removed_it(MessageRemoval removal, bool mine, string expected)
    {
        English();
        var cut = Render<MessageBubble>(p => p.Add(c => c.Mine, mine).Add(c => c.Text, "secret").Add(c => c.Removal, removal));

        Assert.Contains("lms-msg-removed", cut.Find(".lms-msg").ClassList);
        Assert.Contains(expected, cut.Find(".lms-bubble").TextContent);
        Assert.DoesNotContain("secret", cut.Markup);
    }

    [Fact]
    public async Task A_failed_message_says_so_in_words_and_offers_try_again()
    {
        English();
        var retried = false;
        var cut = Render<MessageBubble>(p => p.Add(c => c.Mine, true).Add(c => c.Text, "Hi").Add(c => c.Status, MessageStatus.Failed)
            .Add(c => c.OnRetry, EventCallback.Factory.Create(this, () => retried = true)));

        Assert.Contains("Not sent", cut.Find(".lms-msg-meta").TextContent);
        await cut.Find("[data-testid=msg-retry]").ClickAsync(new MouseEventArgs());
        Assert.True(retried);
    }

    [Fact]
    public async Task Composer_sends_on_ctrl_enter_and_on_the_visible_button_but_not_on_enter()
    {
        English();
        var sent = 0;
        var cut = Render<MessageComposer>(p => p.Add(c => c.Value, "Hello").Add(c => c.OnSend, EventCallback.Factory.Create(this, () => sent++)));

        var field = cut.Find("textarea");
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(0, sent);
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        Assert.Equal(1, sent);
        await field.KeyDownAsync(new KeyboardEventArgs { Key = "Enter", MetaKey = true });
        Assert.Equal(2, sent);
        await cut.Find("[data-testid=composer-send]").ClickAsync(new MouseEventArgs());
        Assert.Equal(3, sent);
        Assert.Contains("Ctrl+Enter", cut.Find("[data-testid=composer-hint]").TextContent);
    }

    [Fact]
    public async Task Composer_does_not_send_an_empty_message_or_while_sending()
    {
        English();
        var sent = 0;
        var empty = Render<MessageComposer>(p => p.Add(c => c.Value, "  ").Add(c => c.OnSend, EventCallback.Factory.Create(this, () => sent++)));
        Assert.True(empty.Find("[data-testid=composer-send]").HasAttribute("disabled"));
        await empty.Find("textarea").KeyDownAsync(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        var busy = Render<MessageComposer>(p => p.Add(c => c.Value, "Hi").Add(c => c.Sending, true).Add(c => c.OnSend, EventCallback.Factory.Create(this, () => sent++)));
        Assert.True(busy.Find("[data-testid=composer-send]").HasAttribute("disabled"));
        Assert.Equal(0, sent);
    }

    [Fact]
    public void Read_only_conversation_replaces_the_composer_with_the_reason()
    {
        English();
        var cut = Render<ComposerOff>(p => p.Add(c => c.Reason, "You can only read this conversation."));

        Assert.Contains("You can only read this conversation.", cut.Find(".lms-composer-off").TextContent);
        Assert.Empty(cut.FindAll("textarea"));
    }

    [Fact]
    public void Thread_body_is_a_log_and_new_messages_are_announced_through_one_polite_status()
    {
        English();
        var cut = Render<ThreadPane>(p => p.Add(c => c.Label, "Huda Al-Harbi").Add(c => c.Announcement, "Huda Al-Harbi: See you")
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddContent(0, "messages"))));

        var log = cut.Find(".lms-thread-body");
        Assert.Equal("log", log.GetAttribute("role"));
        Assert.Equal("off", log.GetAttribute("aria-live")); // own messages are not read out; the status below says other people's, once
        Assert.Equal("0", log.GetAttribute("tabindex"));
        var status = cut.Find("[data-testid=thread-announce]");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Equal("Huda Al-Harbi: See you", status.TextContent.Trim());
    }

    [Fact]
    public void Inbox_shows_both_panes_and_marks_the_thread_pane_for_narrow_screens()
    {
        English();
        var cut = Render<InboxLayout>(p => p.Add(c => c.ShowThread, true)
            .Add(c => c.List, (RenderFragment)(b => b.AddContent(0, "list")))
            .Add(c => c.Thread, (RenderFragment)(b => b.AddContent(0, "thread"))));

        var inbox = cut.Find(".lms-inbox");
        Assert.Contains("show-thread", inbox.ClassList);
        Assert.Contains("list", cut.Find(".lms-inbox-list").TextContent);
        Assert.Contains("thread", cut.Find(".lms-inbox-thread").TextContent);
    }

    [Fact]
    public void Pinned_announcement_has_its_icon_chip_and_heading()
    {
        English();
        var cut = Render<AnnouncementCard>(p => p.Add(c => c.Title, "Parents' evening").Add(c => c.Body, "Thursday at 6.").Add(c => c.Pinned, true)
            .Add(c => c.ReadCount, "Read by 412 of 590"));

        Assert.Contains("pinned", cut.Find("article.lms-ann").ClassList);
        Assert.Equal("Parents' evening", cut.Find("h2.lms-ann-title").TextContent.Trim());
        Assert.Contains("Pinned", cut.Find("[data-testid=ann-pinned]").TextContent);
        Assert.Contains("Read by 412 of 590", cut.Find("[data-testid=ann-reads]").TextContent);
        Assert.Equal("auto", cut.Find(".lms-ann-text").GetAttribute("dir"));
    }

    [Fact]
    public async Task Long_announcement_is_clamped_with_read_more_that_expands_it()
    {
        English();
        var cut = Render<AnnouncementCard>(p => p.Add(c => c.Title, "T").Add(c => c.Body, new string('x', 600)));

        Assert.Contains("lms-ann-clamp", cut.Find(".lms-ann-text").ClassList);
        var more = cut.Find("[data-testid=ann-more]");
        Assert.Equal("false", more.GetAttribute("aria-expanded"));
        await more.ClickAsync(new MouseEventArgs());
        Assert.DoesNotContain("lms-ann-clamp", cut.Find(".lms-ann-text").ClassList);
        Assert.Equal("true", cut.Find("[data-testid=ann-more]").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void Arabic_words_come_from_resources()
    {
        FakeCultureService.Apply(new CultureInfo("ar"));
        var cut = Render<MessageBubble>(p => p.Add(c => c.Mine, true).Add(c => c.Text, "x").Add(c => c.Removal, MessageRemoval.BySender));

        Assert.Contains("حذفت هذه الرسالة.", cut.Find(".lms-bubble").TextContent);
    }
}
