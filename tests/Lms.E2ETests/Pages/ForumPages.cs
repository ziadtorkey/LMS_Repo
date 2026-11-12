using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>Types into a rich text editor (Quill) the way a user does; <paramref name="testId"/> is the editor's root test id.</summary>
/// <summary>MudSwitch helpers: the test id may sit on the root or on the checkbox input, so the state is read from the input and the click goes to its label.</summary>
internal static class Switches
{
    public static async Task SetAsync(ILocator toggle, bool on)
    {
        var input = toggle.Locator("xpath=descendant-or-self::input[@type='checkbox']");
        if (await input.IsCheckedAsync() != on)
        {
            await toggle.Locator("xpath=ancestor-or-self::label").ClickAsync();
            await Microsoft.Playwright.Assertions.Expect(input).ToBeCheckedAsync(new() { Checked = on });
        }
    }
}

internal static class Editors
{
    public static async Task TypeAsync(IPage page, string testId, string text)
    {
        var editor = page.GetByTestId(testId).Locator(".ql-editor");
        await editor.ClickAsync();
        await editor.PressSequentiallyAsync(text);
    }
}

/// <summary>The course forum (/class-courses/{id}/forum): the thread list and the "new thread" composer.</summary>
public sealed class ForumPage(IPage page)
{
    public ILocator Threads => page.GetByTestId("forum-threads");
    public ILocator Empty => page.GetByTestId("forum-empty");
    public ILocator NewThread => page.GetByTestId("forum-new");
    public ILocator NewTitle => page.GetByTestId("forum-new-title");
    public ILocator NewError => page.GetByTestId("forum-form-error");

    /// <summary>The row of the thread with this title.</summary>
    public ILocator Thread(string title) => page.Locator("[data-testid^='forum-thread-'][class*='mud-paper']").Filter(new LocatorFilterOptions { HasText = title });

    public ILocator ThreadLink(string title) => page.Locator("[data-testid^='forum-thread-link-']").Filter(new LocatorFilterOptions { HasText = title });

    public ILocator Pinned(string title) => Thread(title).Locator("[data-testid^='forum-pinned-']");

    public ILocator Locked(string title) => Thread(title).Locator("[data-testid^='forum-locked-']");

    public async Task OpenAsync(Guid classCourseId)
    {
        await page.GotoAsync($"/class-courses/{classCourseId}/forum");
        await page.Locator("[data-testid='forum-threads'], [data-testid='forum-empty'], [data-testid='forum-forbidden'], [data-testid='forum-error']")
            .First.WaitForAsync();
    }

    /// <summary>Starts a thread: title, first post, send. Waits until the thread is listed.</summary>
    public async Task StartThreadAsync(string title, string body)
    {
        await NewThread.ClickAsync();
        await NewTitle.FillAsync(title);
        await Editors.TypeAsync(page, "forum-new-composer-editor", body);
        await page.GetByTestId("forum-new-composer-send").ClickAsync();
        await ThreadLink(title).WaitForAsync();
    }
}

/// <summary>One forum thread (/class-courses/{id}/forum/{threadId}): posts, the reply composer, moderation.</summary>
public sealed class ThreadPage(IPage page)
{
    public ILocator Title => page.GetByTestId("thread-title");
    public ILocator PinnedChip => page.GetByTestId("thread-pinned");
    public ILocator LockedChip => page.GetByTestId("thread-locked");
    public ILocator LockedNote => page.GetByTestId("thread-locked-note");
    public ILocator ModerateMenu => page.GetByTestId("thread-moderate");
    public ILocator Pin => page.GetByTestId("thread-pin");
    public ILocator Lock => page.GetByTestId("thread-lock");
    public ILocator ReplyComposer => page.GetByTestId("thread-reply");
    public ILocator ReplyEditor => page.GetByTestId("thread-reply-editor");
    public ILocator ActionError => page.GetByTestId("thread-action-error");

    public async Task WaitReadyAsync() =>
        await page.Locator("[data-testid='thread-title'], [data-testid='thread-error'], [data-testid='thread-forbidden']").First.WaitForAsync();

    /// <summary>Pins or unpins (the menu item toggles) and waits for the menu to close.</summary>
    public async Task TogglePinAsync()
    {
        await ModerateMenu.ClickAsync();
        await Pin.ClickAsync();
    }

    public async Task ToggleLockAsync()
    {
        await ModerateMenu.ClickAsync();
        await Lock.ClickAsync();
    }
}

/// <summary>The course announcements (/class-courses/{id}/announcements): the publish form (moderators) and the list.</summary>
public sealed class AnnouncementsPage(IPage page)
{
    public ILocator Form => page.GetByTestId("announcement-form");
    public ILocator Title => page.GetByTestId("announcement-title");
    public ILocator Pin => page.GetByTestId("announcement-pin");
    public ILocator Notify => page.GetByTestId("announcement-notify");
    public ILocator Send => page.GetByTestId("announcement-send");
    public ILocator FormError => page.GetByTestId("announcement-form-error");
    public ILocator List => page.GetByTestId("announcements-list");

    public ILocator Item(string title) => page.Locator("[data-testid^='announcement-'][class*='mud-paper']").Filter(new LocatorFilterOptions { HasText = title });

    public ILocator PinnedChip(string title) => Item(title).Locator("[data-testid^='announcement-pinned-']");

    public async Task OpenAsync(Guid classCourseId)
    {
        await page.GotoAsync($"/class-courses/{classCourseId}/announcements");
        await page.Locator("[data-testid='announcements-list'], [data-testid='announcements-empty'], [data-testid='announcements-forbidden'], [data-testid='announcements-error']")
            .First.WaitForAsync();
    }

    /// <summary>Publishes an announcement. The switches are set explicitly so the scenario does not depend on their defaults.</summary>
    public async Task PublishAsync(string title, string body, bool pin, bool notify)
    {
        await Title.FillAsync(title);
        await Editors.TypeAsync(page, "announcement-editor", body);
        await Switches.SetAsync(Pin, pin);
        await Switches.SetAsync(Notify, notify);
        await Send.ClickAsync();
        await Item(title).WaitForAsync();
    }
}
