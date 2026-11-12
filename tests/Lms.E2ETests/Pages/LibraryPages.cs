using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The digital library (/library): the card grid, the create / edit dialog, "add to lesson" and the reader.</summary>
public sealed class LibraryPage(IPage page)
{
    public ILocator Grid => page.GetByTestId("library-grid");
    public ILocator Empty => page.GetByTestId("library-empty");
    public ILocator New => page.GetByTestId("library-new");
    public ILocator Search => page.GetByTestId("library-search").Locator("input");

    /// <summary>The card of the resource with this (English or Arabic) title.</summary>
    public ILocator Card(string title) => page.Locator("[data-testid^='library-card-']").Filter(new LocatorFilterOptions { HasText = title });

    public ILocator Open(string title) => Card(title).Locator("[data-testid^='library-open-']");

    public ILocator AddToLesson(string title) => Card(title).Locator("[data-testid^='library-add-']");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/library");
        await page.Locator("[data-testid='library-grid'], [data-testid='library-empty'], [data-testid='library-forbidden']").First.WaitForAsync();
    }

    /// <summary>Opens the create dialog.</summary>
    public async Task<LibraryResourceDialogPage> NewAsync()
    {
        await New.ClickAsync();
        var dialog = new LibraryResourceDialogPage(page);
        await dialog.Save.WaitForAsync();
        return dialog;
    }

    /// <summary>Opens the reader of a resource (the protected viewer; a download button only when the resource allows it).</summary>
    public async Task<LibraryReader> ReadAsync(string title)
    {
        await Open(title).ClickAsync();
        var reader = new LibraryReader(page);
        await reader.Root.WaitForAsync();
        return reader;
    }

    /// <summary>Opens "add to lesson" for a resource.</summary>
    public async Task<AddToLessonDialogPage> AddToLessonAsync(string title)
    {
        await AddToLesson(title).ClickAsync();
        var dialog = new AddToLessonDialogPage(page);
        await dialog.Save.WaitForAsync();
        return dialog;
    }
}

/// <summary>The create / edit resource dialog of the library page.</summary>
public sealed class LibraryResourceDialogPage(IPage page)
{
    public ILocator Save => page.GetByTestId("library-save");
    public ILocator Error => page.GetByTestId("library-form-error");
    public ILocator TitleAr => page.GetByTestId("library-title-ar").Locator("input");
    public ILocator TitleEn => page.GetByTestId("library-title-en").Locator("input");
    public ILocator Download => page.GetByTestId("library-form-download");
    public ILocator FileInput => page.Locator("[data-testid='asset-input']").First;

    /// <summary>The unit picker lists the visible units as an indented tree; the first entry is the root (the school).</summary>
    public async Task PickRootUnitAsync()
    {
        await page.GetByTestId("library-unit").ClickAsync();
        await page.Locator(".mud-popover-open .mud-list-item").First.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item")).ToHaveCountAsync(0);
    }

    public async Task SetTitlesAsync(string ar, string en)
    {
        await TitleAr.FillAsync(ar);
        await TitleEn.FillAsync(en);
    }

    /// <summary>Uploads the file of the resource and waits until the uploader reports it.</summary>
    public async Task UploadFileAsync(string fileName, string contentType, byte[] bytes)
    {
        await FileInput.SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = contentType, Buffer = bytes });
        await Microsoft.Playwright.Assertions.Expect(page.GetByTestId("asset-status").First).ToBeVisibleAsync();
    }

    /// <summary>Saves and waits until the dialog has closed.</summary>
    public async Task SaveAsync()
    {
        await Save.ClickAsync();
        await Save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}

/// <summary>The "add to lesson" dialog: class course, lesson and completion rule, then add.</summary>
public sealed class AddToLessonDialogPage(IPage page)
{
    public ILocator Save => page.GetByTestId("add-lesson-save");
    public ILocator Error => page.GetByTestId("add-lesson-error");

    public async Task PickAsync(string testId, string optionText)
    {
        await page.GetByTestId(testId).ClickAsync();
        var option = page.Locator(".mud-popover-open .mud-list-item").Filter(new LocatorFilterOptions { HasText = optionText }).First;
        await option.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator(".mud-popover-open .mud-list-item")).ToHaveCountAsync(0);
    }

    /// <summary>Picks the class course (by its course name), then the lesson, and adds; waits until the dialog has closed.</summary>
    public async Task AddAsync(string courseText, string lessonText)
    {
        await PickAsync("add-lesson-course", courseText);
        await Microsoft.Playwright.Assertions.Expect(page.GetByTestId("add-lesson-lesson")).ToBeEnabledAsync();
        await PickAsync("add-lesson-lesson", lessonText);
        await Save.ClickAsync();
        await Save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}

/// <summary>The resource reader dialog: the protected preview, a download button only when allowed.</summary>
public sealed class LibraryReader(IPage page)
{
    public ILocator Root => page.GetByTestId("library-reader");
    public ILocator Download => page.GetByTestId("library-download");
    public ILocator Error => page.GetByTestId("library-reader-error");
}
