using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The moderation queue and its review panel (/moderation/messages, M6 FE2; ids from docs/m6-e2e-testids-fe2.md).</summary>
public sealed class ModerationQueuePage(IPage page)
{
    public ILocator Title => page.GetByTestId("mod-title");
    public ILocator Table => page.GetByTestId("mod-table");
    public ILocator Rows => page.Locator("[data-testid^='mod-row-']");
    public ILocator Row(Guid id) => page.GetByTestId($"mod-row-{id}");
    public ILocator Status(Guid id) => page.GetByTestId($"mod-status-{id}");
    public ILocator Panel => page.GetByTestId("mod-panel");
    public ILocator FrozenContext => page.GetByTestId("mod-frozen");
    public ILocator Context => page.GetByTestId("mod-context");
    public ILocator Resolved => page.GetByTestId("mod-resolved");
    public ILocator Restrictions => page.GetByTestId("mod-restrictions");
    public ILocator Restriction(Guid userId) => page.GetByTestId($"mod-restriction-{userId}");
    public ILocator Error => page.GetByTestId("mod-error");

    public async Task OpenAsync()
    {
        await page.GotoAsync("/moderation/messages");
        await Title.WaitForAsync();
    }

    /// <summary>The first row marked as a safety concern (they sort to the top, D66).</summary>
    public ILocator FirstRowIsSafety => Rows.First;

    public Task OpenReportAsync(Guid id) => page.GetByTestId($"mod-open-{id}").ClickAsync();

    /// <summary>Picks the decision (<c>mod-{Action}</c>, e.g. RemoveAndRestrict), writes the note to the people and applies it.</summary>
    public async Task DecideAsync(string action, string noteToPeople, int? days = null)
    {
        await page.GetByTestId($"mod-{action}").ClickAsync();
        if (days is not null)
        {
            await page.GetByTestId("mod-days").ClickAsync();
            await page.GetByRole(AriaRole.Option, new PageGetByRoleOptions { Name = days.Value.ToString() }).First.ClickAsync();
        }

        await page.Locator("[data-testid='mod-note-to-people'] textarea, [data-testid='mod-note-to-people'] input").Locator("visible=true").First.FillAsync(noteToPeople);
        await page.GetByTestId("mod-apply").ClickAsync();
    }

    public Task LiftAsync(Guid userId) => page.GetByTestId($"mod-lift-{userId}").ClickAsync();
}
