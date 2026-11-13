using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>
/// The teacher's package upload dialog (M4b Task 16, #501): drop zone, upload progress, the validation status, the reject reason and the
/// attach form. Opened from the lesson plan's "add module" menu (<see cref="LessonPlanPage.AddMenu"/>). Locators follow the test ids on main.
/// </summary>
public sealed class PackageUploadDialog(IPage page)
{
    public ILocator Dialog => page.GetByTestId("package-dialog");
    public ILocator Dropzone => page.GetByTestId("package-dropzone");
    public ILocator Input => page.GetByTestId("package-input").Locator("input[type=file]").Or(page.Locator("input[type=file]")).First;
    public ILocator Limits => page.GetByTestId("package-limits");
    public ILocator Uploading => page.GetByTestId("package-uploading");
    public ILocator Checking => page.GetByTestId("package-checking");
    public ILocator CheckAgain => page.GetByTestId("package-check-again");
    public ILocator Error => page.GetByTestId("package-error");
    public ILocator Blocked => page.GetByTestId("package-blocked");
    public ILocator Close => page.GetByTestId("package-close");

    /// <summary>The rejected state: the plain-words reason of the reject code (<c>Error_package_reason_*</c>).</summary>
    public ILocator Reject => page.GetByTestId("package-reject");
    public ILocator RejectReason => page.GetByTestId("package-reject-reason");
    public ILocator ChooseAnother => page.GetByTestId("package-choose-another");

    /// <summary>The Ready state: what was found, and the attach form.</summary>
    public ILocator Summary => page.GetByTestId("package-summary");
    public ILocator FoundTitle => page.GetByTestId("package-found-title");
    public ILocator FoundType => page.GetByTestId("package-found-type");
    public ILocator Lesson => page.GetByTestId("package-lesson");
    public ILocator Rule => page.GetByTestId("package-rule");
    public ILocator TitleAr => page.GetByTestId("package-title-ar").Locator("input");
    public ILocator TitleEn => page.GetByTestId("package-title-en").Locator("input");
    public ILocator Attach => page.GetByTestId("package-attach");
    public ILocator AttachError => page.GetByTestId("package-attach-error");
    public ILocator Delete => page.GetByTestId("package-delete");

    public Task UploadAsync(string fileName, byte[] bytes) =>
        Input.SetInputFilesAsync(new FilePayload { Name = fileName, MimeType = fileName.EndsWith(".h5p", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "application/zip", Buffer = bytes });

    /// <summary>Attaches the Ready package to the lesson and waits until the dialog closes.</summary>
    public async Task AttachAsync()
    {
        await Attach.ClickAsync();
        await Dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }
}

/// <summary>
/// The package module in the viewer (M4b Task 17 #552 and the D57 re-open of #555): the start card, the sandboxed player with its toolbar,
/// the expired / not-ready / host states, the staff preview and the result card. The frame is cross-origin and sandboxed: the page can
/// only read its attributes (<see cref="FrameElement"/>), never its content; what runs inside is observed through the app (progress,
/// completion, grade) and, for the content itself, through <see cref="Frame"/> (Playwright can enter an iframe).
/// </summary>
public sealed class PackagePlayerPage(IPage page)
{
    // Start card (before launch; nothing loads in a frame until Start).
    public ILocator StartCard => page.GetByTestId("package-start-card");
    public ILocator StartTitle => page.GetByTestId("package-start-title");
    public ILocator StateChip => page.GetByTestId("package-state-chip");
    public ILocator ThirdPartyNote => page.GetByTestId("package-third-party");
    public ILocator PhoneHint => page.GetByTestId("package-phone-hint");
    public ILocator StartScore => page.GetByTestId("package-start-score");
    public ILocator LatestScore => page.GetByTestId("package-latest-score");

    /// <summary>Start, or Resume when an attempt is in progress (the label differs, the test id does not).</summary>
    public ILocator Start => page.GetByTestId("package-start");

    /// <summary>Staff only: opens the preview (records nothing).</summary>
    public ILocator Preview => page.GetByTestId("package-preview");

    // Running player.
    public ILocator Player => page.GetByTestId("package-player");
    public ILocator FrameElement => page.GetByTestId("package-frame");
    public ILocator PreviewBanner => page.GetByTestId("package-preview-banner");
    public ILocator Toolbar => page.GetByTestId("package-toolbar");
    public ILocator ToolbarTitle => page.GetByTestId("package-toolbar-title");
    public ILocator Fullscreen => page.GetByTestId("package-fullscreen");
    public ILocator Exit => page.GetByTestId("package-exit");
    public ILocator SaveState => page.GetByTestId("package-save-state");
    public ILocator SaveRetry => page.GetByTestId("package-save-retry");
    public ILocator PracticeChip => page.GetByTestId("package-practice-chip");

    // States over or instead of the frame.
    public ILocator Launching => page.GetByTestId("package-launching");
    public ILocator Expired => page.GetByTestId("package-expired");
    public ILocator Reload => page.GetByTestId("package-reload");
    public ILocator NotReady => page.GetByTestId("package-not-ready");
    public ILocator HostNotConfigured => page.GetByTestId("package-host-not-configured");
    public ILocator FeatureOff => page.GetByTestId("package-feature-off");
    public ILocator NotAvailable => page.GetByTestId("package-not-available");
    public ILocator Error => page.GetByTestId("package-error");
    public ILocator Back => page.GetByTestId("package-back");

    // Result card after finish, and the D57 re-open action (#555): the label follows the module's grading method.
    public ILocator ResultCard => page.GetByTestId("package-result-card");
    public ILocator ResultChip => page.GetByTestId("package-result-chip");
    public ILocator ResultScore => page.GetByTestId("package-result-score");
    public ILocator PracticeFinished => page.GetByTestId("package-practice-finished");
    public ILocator Reopen => page.GetByTestId("package-reopen");
    public ILocator ReopenButton => page.GetByTestId("package-reopen-button");
    public ILocator ReopenCaption => page.GetByTestId("package-reopen-caption");

    /// <summary>The content of the package: the player frame, entered by Playwright (the page itself never reads it).</summary>
    public IFrameLocator Frame => page.FrameLocator("[data-testid='package-frame']");

    public async Task OpenAsync(Guid classCourseId, Guid moduleId) =>
        await page.GotoAsync($"/class-courses/{classCourseId}/modules/{moduleId}");

    /// <summary>Starts the package and waits for the frame element.</summary>
    public async Task StartAsync()
    {
        await Start.ClickAsync();
        await FrameElement.WaitForAsync();
    }
}
