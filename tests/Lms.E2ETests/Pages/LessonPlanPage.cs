using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The class lesson plan (/class-courses/{id}/plan): lessons with their modules, and the module authoring controls.</summary>
public sealed class LessonPlanPage(IPage page)
{
    public ILocator Header => page.GetByTestId("plan-header");

    public ILocator Lesson(Guid lessonKey) => page.GetByTestId($"plan-lesson-{lessonKey}");

    /// <summary>The module nodes of a lesson, in the order shown.</summary>
    public ILocator Modules(Guid lessonKey) => Lesson(lessonKey).Locator("[data-testid^='module-'][class*='lms-module-node']");

    public ILocator ModuleTitles(Guid lessonKey) => Modules(lessonKey).GetByTestId("module-title");

    public ILocator AddMenu(Guid lessonKey) => Lesson(lessonKey).GetByTestId("module-add-menu");

    public async Task OpenAsync(Guid classCourseId)
    {
        await page.GotoAsync($"/class-courses/{classCourseId}/plan");
        await Header.WaitForAsync();
    }

    /// <summary>Opens the "add module" menu of a lesson and picks a kind; returns the dialog that opens.</summary>
    public async Task<ModuleDialog> AddAsync(Guid lessonKey, string kind)
    {
        var item = page.GetByTestId($"module-add-{kind}");
        // The popover of the previous add may still be closing and swallow the first click: open again until the item shows.
        for (var attempt = 0; attempt < 3 && !await item.IsVisibleAsync(); attempt++)
        {
            await AddMenu(lessonKey).ClickAsync();
            try { await item.WaitForAsync(new LocatorWaitForOptions { Timeout = 4000 }); } catch (TimeoutException) { }
        }

        await item.ClickAsync();
        var dialog = new ModuleDialog(page);
        await dialog.Save.WaitForAsync();
        return dialog;
    }

    /// <summary>The "move down" control of the module whose node has this id.</summary>
    public ILocator Down(Guid moduleId) => page.GetByTestId($"module-down-{moduleId}");

    public ILocator Up(Guid moduleId) => page.GetByTestId($"module-up-{moduleId}");
}

/// <summary>The add / edit module dialog: common metadata plus the body of the kind.</summary>
public sealed class ModuleDialog(IPage page)
{
    public ILocator Save => page.GetByTestId("module-save");
    public ILocator Error => page.GetByTestId("module-error");
    public ILocator TitleAr => page.GetByTestId("module-title-ar");
    public ILocator TitleEn => page.GetByTestId("module-title-en");
    public ILocator Visibility => page.GetByTestId("module-visibility");
    public ILocator Target => page.GetByTestId("module-target");
    public ILocator Group => page.GetByTestId("module-group");
    public ILocator Url => page.GetByTestId("module-url");
    public ILocator AssetInput => page.GetByTestId("asset-input").Locator("input[type=file]").Or(page.Locator("input[type=file]")).First;
    public ILocator AssetName => page.GetByTestId("module-asset-name");
    public ILocator AssetStatus => page.GetByTestId("asset-status");
    public ILocator AssetError => page.GetByTestId("asset-error");

    public async Task SetTitlesAsync(string ar, string en)
    {
        await TitleAr.FillAsync(ar);
        await TitleEn.FillAsync(en);
    }

    /// <summary>Types into the Quill editor of a Page module body (<paramref name="testId"/> is module-body-ar or module-body-en).</summary>
    public async Task TypeBodyAsync(string testId, string text)
    {
        var editor = page.GetByTestId(testId).Locator(".ql-editor");
        await editor.ClickAsync();
        await editor.PressSequentiallyAsync(text);
        await page.WaitForTimeoutAsync(400); // the editor reports its text after a 250 ms debounce; save only after the model has it
    }

    public Task UploadAsync(string fileName, string contentType, byte[] bytes) =>
        AssetInput.SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = contentType, Buffer = bytes });

    /// <summary>Saves and waits until the dialog has closed.</summary>
    public async Task SaveAsync()
    {
        await Save.ClickAsync();
        await Save.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle); // the plan reloads after a save: let it settle before the next menu click
        await page.WaitForTimeoutAsync(300);
    }
}
