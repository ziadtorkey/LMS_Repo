using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The import wizard (FE Task 23, #195) as shown on /staff/import (FE Task 26): template, upload, review, commit.</summary>
public sealed class ImportWizardPage(IPage page)
{
    public ILocator Summary => page.GetByTestId("import-summary");
    public ILocator Blocked => page.GetByTestId("import-blocked");
    public ILocator Failed => page.GetByTestId("import-failed");
    public ILocator Done => page.GetByTestId("import-done");
    public ILocator CommitButton => page.GetByTestId("import-commit");

    public async Task OpenAsync(string path = "/staff/import")
    {
        await page.GotoAsync(path);
        await page.GetByTestId("import-wizard").WaitForAsync(); // a full page load takes about 11 s (#275)
    }

    /// <summary>Picks the template language, downloads the template and returns where it was saved.</summary>
    public async Task<string> DownloadTemplateAsync(string language, string saveTo)
    {
        await page.GetByTestId($"import-lang-{language}").ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(async () => await page.GetByTestId("import-template").ClickAsync());
        await download.SaveAsAsync(saveTo);
        return saveTo;
    }

    public async Task NextAsync() => await page.GetByTestId("import-next").ClickAsync();

    /// <summary>Chooses the target unit (by its shown name) and the file, uploads, and waits for the review summary.</summary>
    public async Task UploadAsync(string filePath, string targetUnitName)
    {
        await page.GetByTestId("import-step-upload").WaitForAsync();
        await Pickers.PickAsync(page, "import-unit", targetUnitName);
        await page.Locator("input[type='file']").SetInputFilesAsync(filePath);
        await page.GetByTestId("import-upload").ClickAsync();
        await Summary.WaitForAsync(new LocatorWaitForOptions { Timeout = 60_000 });
        // Validation runs as a job: the summary is final once the working note has gone.
        await page.GetByTestId("import-working").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 60_000 });
    }

    /// <summary>Downloads the error report of the reviewed batch.</summary>
    public async Task<string> DownloadErrorReportAsync(string saveTo)
    {
        var download = await page.RunAndWaitForDownloadAsync(async () => await page.GetByTestId("import-error-report").ClickAsync());
        await download.SaveAsAsync(saveTo);
        return saveTo;
    }

    /// <summary>Discards the reviewed batch (confirming the dialog if one appears) and returns to the upload step.</summary>
    public async Task DiscardAsync()
    {
        await page.GetByTestId("import-discard").ClickAsync();
        var yes = page.GetByTestId("confirm-yes");
        if (await yes.CountAsync() > 0 || await WaitVisibleAsync(yes))
        {
            await yes.ClickAsync();
        }
    }

    /// <summary>Commits the reviewed batch through the confirmation dialog; returns once the wizard reports an outcome.</summary>
    public async Task CommitAsync()
    {
        await CommitButton.ClickAsync();
        await page.GetByTestId("confirm-yes").ClickAsync();
        await Done.Or(Failed).First.WaitForAsync(new LocatorWaitForOptions { Timeout = 90_000 });
    }

    private static async Task<bool> WaitVisibleAsync(ILocator locator)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = 2_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
