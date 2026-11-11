using Lms.E2ETests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Lms.E2ETests.Pages;

/// <summary>The Settings and Policies tabs of a unit's page (/org-units/{id}, FE Tasks 21 and 22, #62).</summary>
public sealed class UnitConfigPage(IPage page, string culture)
{
    public async Task OpenSettingsAsync(string unitId) => await OpenAsync(unitId, "UnitSettings_Settings", "settings-group-General");

    public async Task OpenPoliciesAsync(string unitId) => await OpenAsync(unitId, "UnitSettings_Policies", "policy-row-CreateOrgUnits");

    // --- settings ---
    public ILocator SettingInput(string name) =>
        page.Locator($"input[data-testid='setting-input-{name}'], [data-testid='setting-input-{name}'] input").First;

    public ILocator SettingSource(string name) => page.GetByTestId($"setting-source-{name}");

    private ILocator SettingLock(string name) =>
        page.Locator($"input[data-testid='setting-lock-{name}'], [data-testid='setting-lock-{name}'] input").First;

    /// <summary>Sets a number and waits for the server state to come back (the source line says "set here").</summary>
    public async Task SetNumberAsync(string name, int value)
    {
        var input = SettingInput(name);
        await input.FillAsync(value.ToString());
        await input.PressAsync("Tab");
        await Expect(SettingSource(name)).ToContainTextAsync(Resx.In(culture, "Settings_SetHere"));
    }

    public async Task LockAsync(string name)
    {
        await SettingLock(name).CheckAsync();
        await Expect(SettingLock(name)).ToBeCheckedAsync();
    }

    public async Task ToggleFlagAsync(string name)
    {
        await SettingInput(name).ClickAsync();
        await Expect(SettingSource(name)).ToContainTextAsync(Resx.In(culture, "Settings_SetHere"));
    }

    /// <summary>Removes the value set at this unit, lock included (no-op when it is not set here).</summary>
    public async Task ResetAsync(string name)
    {
        var reset = page.GetByTestId($"setting-reset-{name}");
        if (await reset.CountAsync() > 0)
        {
            await reset.ClickAsync();
            await Expect(reset).ToHaveCountAsync(0);
        }
    }

    // --- policies ---
    public ILocator PolicySwitch(string key) =>
        page.Locator($"input[data-testid='policy-switch-{key}'], [data-testid='policy-switch-{key}'] input").First;

    public async Task SetPolicyAsync(string key, bool on)
    {
        if (await PolicySwitch(key).IsCheckedAsync() != on)
        {
            await PolicySwitch(key).SetCheckedAsync(on);
        }

        await Expect(PolicySwitch(key)).ToBeCheckedAsync(new() { Checked = on });
    }

    private async Task OpenAsync(string unitId, string tabKey, string readyTestId)
    {
        await page.GotoAsync($"/org-units/{unitId}");
        await page.GetByTestId("unit-tabs").WaitForAsync();
        await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = Resx.In(culture, tabKey) }).ClickAsync();
        await page.GetByTestId(readyTestId).WaitForAsync();
    }
}
