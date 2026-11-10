using Microsoft.Playwright;

namespace Lms.E2ETests.Pages;

/// <summary>The Blazor shell: app bar, side navigation, language switch, context switcher.</summary>
public sealed class ShellPage(IPage page)
{
    public static readonly string[] NavKeys = ["home", "orgunits", "roles", "settings", "audit", "clients"];

    public ILocator Root => page.GetByTestId("app-root");
    public ILocator LanguageSwitch => page.GetByTestId("lang-switch");
    public ILocator ContextSwitcher => page.GetByTestId("context-switcher");
    public ILocator UserMenu => page.GetByTestId("user-menu");
    public ILocator NavToggle => page.GetByTestId("nav-toggle");
    public ILocator ForbiddenPage => page.GetByTestId("forbidden-page");

    public ILocator Nav(string key) => page.GetByTestId($"nav-{key}");

    /// <summary>Waits until the shell has rendered with the menu loaded (the Home entry is always present).</summary>
    public async Task WaitReadyAsync()
    {
        await Root.WaitForAsync();
        await Nav("home").WaitForAsync();
    }

    /// <summary>Keys of the nav entries currently rendered.</summary>
    public async Task<IReadOnlyList<string>> VisibleNavKeysAsync()
    {
        var keys = new List<string>();
        foreach (var key in NavKeys)
        {
            if (await Nav(key).CountAsync() > 0)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    public Task<string?> HtmlDirAsync() => page.Locator("html").GetAttributeAsync("dir");

    public Task<string?> HtmlLangAsync() => page.Locator("html").GetAttributeAsync("lang");

    public Task<string?> RootDirAsync() => Root.GetAttributeAsync("dir");

    /// <summary>Switches the UI language; the app persists the choice and reloads.</summary>
    public async Task SwitchLanguageAsync()
    {
        var before = await HtmlLangAsync();
        await LanguageSwitch.ClickAsync();
        await page.WaitForFunctionAsync("lang => document.documentElement.lang !== lang", before);
        await WaitReadyAsync();
    }
}
