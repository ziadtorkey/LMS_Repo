using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The announcement composer (/announcements/new, M6 FE2; ids verified against AnnouncementComposerForm.razor and docs/m6-e2e-testids-fe2.md).</summary>
public sealed class AnnouncementComposerPage(IPage page)
{
    public ILocator Root => page.GetByTestId("ann-compose");
    public ILocator Error => page.GetByTestId("annc-error");
    public ILocator TargetsError => page.GetByTestId("ann-targets-error");
    public ILocator Reach => page.GetByTestId("reach");
    public ILocator Timezone => page.GetByTestId("ann-tz");
    public ILocator Publish => page.GetByTestId("ann-publish");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/announcements/new");
        await Root.WaitForAsync();
    }

    /// <summary>Both titles and the body are written by the author: ar and en titles are required, at least one body.</summary>
    public async Task WriteAsync(string titleAr, string titleEn, string bodyAr, string bodyEn)
    {
        await Input("ann-title-ar").FillAsync(titleAr);
        await Input("ann-title-en").FillAsync(titleEn);
        await Input("ann-body-ar").FillAsync(bodyAr);
        await Input("ann-body-en").FillAsync(bodyEn);
    }

    /// <summary>The audience scope chip: <c>Organization</c>, <c>Unit</c>, <c>Level</c>, <c>Class</c>, <c>ClassCourse</c> or <c>Group</c> (what the author may use is decided by the server).</summary>
    public Task ChooseScopeAsync(string kind) => page.GetByTestId($"ann-scope-{kind}").ClickAsync();

    /// <summary>Picks the target in the audience select (a class or course the author teaches); the text is the visible option.</summary>
    public Task ChooseTargetAsync(string optionText) => MudControls.SelectAsync(page, page.GetByTestId("audience-select"), optionText);

    /// <summary>The role chips <c>Staff</c>, <c>Students</c>, <c>Guardians</c>; the Guardians chip defaults on for unit authors and off for a teacher's course post (L14).</summary>
    public ILocator RoleChip(string role) => page.GetByTestId($"role-{role}");

    public async Task SetRoleAsync(string role, bool on)
    {
        var chip = RoleChip(role);
        if (await IsOnAsync(chip) != on)
        {
            await chip.ClickAsync();
        }
    }

    public Task PublishNowAsync() => page.GetByTestId("ann-mode-now").ClickAsync();

    /// <summary>Schedule in the client zone: the date and time are the client's local ones (<see cref="Timezone"/> names the zone).</summary>
    public async Task ScheduleAsync(DateOnly date, TimeOnly time)
    {
        await page.GetByTestId("ann-mode-schedule").ClickAsync();
        await MudControls.PickDateAsync(page, page.GetByTestId("ann-date"), date);
        await Input("ann-time").FillAsync(time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
    }

    public Task SaveDraftAsync() => page.GetByTestId("ann-save-draft").ClickAsync();

    public Task SubmitAsync() => Publish.ClickAsync();

    public Task CancelAsync() => page.GetByTestId("ann-cancel").ClickAsync();

    public ILocator Toggle(string testId) => page.GetByTestId(testId); // ann-pin, ann-notify

    private ILocator Input(string testId) => page.GetByTestId(testId).Locator("input, textarea").First;

    private static async Task<bool> IsOnAsync(ILocator chip) =>
        await chip.GetAttributeAsync("aria-pressed") == "true" || await chip.GetAttributeAsync("aria-checked") == "true"
        || (await chip.GetAttributeAsync("class") ?? string.Empty).Contains("selected", StringComparison.OrdinalIgnoreCase);
}
