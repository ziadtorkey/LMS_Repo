using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The new-message dialog and its recipient picker (M6 FE1 Task 22, #757): ids <c>compose*</c>, <c>picker*</c> and the composer ids. The server returns only the
/// people the D69 rules allow, so "cannot find" means the person is absent from <see cref="Options"/> with or without a search (spec #615 §3.2).
/// </summary>
public sealed class NewConversationDialog(IPage page)
{
    public ILocator Dialog => page.GetByTestId("compose");
    public ILocator Error => page.GetByTestId("compose-error");
    public ILocator KindGroup => page.GetByTestId("compose-kind");
    public ILocator DirectKind => page.GetByTestId("compose-direct");
    public ILocator GroupKind => page.GetByTestId("compose-group");
    public ILocator Title => page.GetByTestId("compose-title");
    public ILocator MinPeople => page.GetByTestId("compose-min");
    public ILocator Cancel => page.GetByTestId("compose-cancel");

    public ILocator Picker => page.GetByTestId("picker");
    public ILocator Search => page.GetByTestId("picker-search");
    public ILocator Hint => page.GetByTestId("picker-hint");
    public ILocator List => page.GetByTestId("picker-list");
    public ILocator None => page.GetByTestId("picker-none");
    public ILocator Limit => page.GetByTestId("picker-limit");

    /// <summary>The one D69 row a manager or Org Admin gets in place of any student (no private conversation with a student).</summary>
    public ILocator SafeguardingRow => page.GetByTestId("picker-d69");

    public ILocator Option(Guid userId) => page.GetByTestId($"picker-{userId}");
    public ILocator Options => List.Locator("button[data-testid^='picker-']:not([data-testid='picker-list'])");

    public ILocator ComposerText => Dialog.GetByTestId("composer-text");
    public ILocator Send => Dialog.GetByTestId("composer-send");

    public async Task OpenAsync(MessagesPage messages)
    {
        await messages.NewMessageButton.ClickAsync();
        await Dialog.WaitForAsync();
        await Picker.WaitForAsync();
    }

    /// <summary>The names the picker lists right now (the whole allowed set, or the search result).</summary>
    public async Task<IReadOnlyList<string>> OptionNamesAsync() => await Options.AllInnerTextsAsync();

    /// <summary>Searches by name (debounced) and waits for the list to settle.</summary>
    public async Task SearchAsync(string text)
    {
        await Search.Locator("input").FillAsync(text);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    /// <summary>Picks one person and sends the first message: the dialog closes and the thread opens.</summary>
    public async Task StartDirectAsync(Guid userId, string firstMessage)
    {
        await Option(userId).ClickAsync();
        await ComposerText.FillAsync(firstMessage);
        await Send.ClickAsync();
        await Expect(Dialog).ToBeHiddenAsync();
    }

    public async Task StartGroupAsync(string title, IEnumerable<Guid> userIds, string firstMessage)
    {
        await GroupKind.ClickAsync();
        await Title.Locator("input").FillAsync(title);
        foreach (var id in userIds)
        {
            await Option(id).ClickAsync();
        }

        await ComposerText.FillAsync(firstMessage);
        await Send.ClickAsync();
        await Expect(Dialog).ToBeHiddenAsync();
    }
}
