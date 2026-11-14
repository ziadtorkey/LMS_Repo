using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The messages page (/messages and /messages/{id}, M6 FE1 Tasks 21, 22, 23 and 24). Ids come from the FE1 pages and components on #753, #757, #761 and
/// #763 (<c>Pages/Messaging</c>, <c>Components/Messaging</c>); the realtime ids (<c>realtime-offline</c>, <c>messages-announcer</c>, <c>thread-jump</c>) are #763's.
/// </summary>
public sealed class MessagesPage(IPage page)
{
    // Inbox
    public ILocator Title => page.GetByTestId("messages-title");
    public ILocator Inbox => page.GetByTestId("inbox");
    public ILocator InboxList => page.GetByTestId("inbox-list");
    public ILocator InboxEmpty => page.GetByTestId("inbox-empty");
    public ILocator Row(Guid conversationId) => page.GetByTestId($"conv-{conversationId}");
    public ILocator Rows => InboxList.Locator("a[data-testid^='conv-']");
    public ILocator Search => page.GetByTestId("inbox-search");
    public ILocator SearchHits => page.GetByTestId("inbox-hits");
    public ILocator NewMessageButton => page.GetByTestId("messages-new");
    public ILocator Unavailable => page.GetByTestId("messages-unavailable");
    public ILocator Forbidden => page.GetByTestId("messages-forbidden");

    // Thread
    public ILocator Thread => page.GetByTestId("thread");
    public ILocator ThreadBody => page.GetByTestId("thread-body");
    public ILocator Header => page.GetByTestId("thread-header");
    public ILocator Back => page.GetByTestId("thread-back");
    public ILocator Divider => page.GetByTestId("thread-divider");
    public ILocator LoadEarlier => page.GetByTestId("thread-earlier");
    public ILocator Message(Guid messageId) => page.GetByTestId($"msg-{messageId}");
    public ILocator Bubbles => ThreadBody.Locator("[data-testid^='msg-']:not([data-testid^='msg-delete-']):not([data-testid^='msg-report-']):not([data-testid^='msg-reported-'])");
    public ILocator DeleteButton(Guid messageId) => page.GetByTestId($"msg-delete-{messageId}");
    public ILocator ReportButton(Guid messageId) => page.GetByTestId($"msg-report-{messageId}");
    public ILocator ReportedMark(Guid messageId) => page.GetByTestId($"msg-reported-{messageId}");
    public ILocator SendError => page.GetByTestId("thread-send-error");
    public ILocator ReplyPrivately => page.GetByTestId("thread-reply-privately");
    public ILocator Members => page.GetByTestId("thread-members");
    public ILocator More => page.GetByTestId("thread-more");
    public ILocator BlockItem => page.GetByTestId("thread-block");
    public ILocator UnblockItem => page.GetByTestId("thread-unblock");

    // Composer
    public ILocator Composer => page.GetByTestId("composer");
    public ILocator ComposerText => page.GetByTestId("composer-text");
    public ILocator ComposerSend => page.GetByTestId("composer-send");
    public ILocator ComposerHint => page.GetByTestId("composer-hint");
    public ILocator ComposerOff => page.GetByTestId("composer-off");

    // Realtime (#763)
    public ILocator Offline => page.GetByTestId("realtime-offline");
    public ILocator Announcer => page.GetByTestId("messages-announcer");
    public ILocator JumpToNew => page.GetByTestId("thread-jump");

    public async Task OpenInboxAsync()
    {
        await page.GotoAsync("/messages");
        await Title.WaitForAsync();
    }

    public async Task OpenAsync(Guid conversationId)
    {
        await page.GotoAsync($"/messages/{conversationId}");
        await Thread.WaitForAsync();
    }

    /// <summary>Opens a conversation from its inbox row, as a user does (works at 360 px, where the thread replaces the list).</summary>
    public async Task OpenFromInboxAsync(Guid conversationId)
    {
        await Row(conversationId).ClickAsync();
        await Thread.WaitForAsync();
    }

    /// <summary>The conversation id from the address bar (after a create or a deep link).</summary>
    public Guid CurrentConversationId() =>
        Guid.Parse(Regex.Match(new Uri(page.Url).AbsolutePath, "/messages/([0-9a-fA-F-]{36})").Groups[1].Value);

    /// <summary>Writes and sends with the Send button (always visible; Ctrl+Enter is only a shortcut).</summary>
    public async Task SendAsync(string text)
    {
        await ComposerText.FillAsync(text);
        await ComposerSend.ClickAsync();
        await Expect(Thread).ToContainTextAsync(text);
    }

    /// <summary>The sender's Ctrl+Enter shortcut; Enter alone breaks the line.</summary>
    public async Task SendWithKeyboardAsync(string text)
    {
        await ComposerText.FillAsync(text);
        await ComposerText.PressAsync("Control+Enter");
        await Expect(Thread).ToContainTextAsync(text);
    }

    public async Task<IReadOnlyList<string>> BubbleTextsAsync() => await Bubbles.AllInnerTextsAsync();

    /// <summary>True when the composer is gone and the thread says why (locked, read-only, blocked).</summary>
    public async Task<bool> HasNoComposerAsync() => await ComposerText.CountAsync() == 0 && await ComposerOff.CountAsync() > 0;
}
