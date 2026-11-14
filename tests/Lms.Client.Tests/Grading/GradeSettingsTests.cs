using System.Globalization;
using Bunit;
using Lms.Client.Pages.Settings;
using Lms.Client.Tests.Support;
using MudBlazor;

namespace Lms.Client.Tests.Grading;

/// <summary>The grading settings group with its save bar and the client time zone picker (M5 Task 30, D58, D64).</summary>
public sealed class GradeSettingsTests : StaffTestContext
{
    private static readonly Guid Unit = Guid.NewGuid();

    private async Task PrepareAsync(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(Lms.Shared.Authorization.Permissions.Settings.View);
        RenderProviders();
    }

    private async Task<IRenderedComponent<GradeSettingsGroup>> RenderGroupAsync(string culture = "en", bool canEdit = true)
    {
        await PrepareAsync(culture);
        return Render<GradeSettingsGroup>(p => p.Add(x => x.UnitId, Unit).Add(x => x.CanEdit, canEdit));
    }

    [Fact]
    public async Task Grade_settings_use_the_save_bar()
    {
        var page = await RenderGroupAsync();
        page.WaitForElement("[data-testid=grade-setting-decimals]");
        Assert.Empty(page.FindAll("[data-testid=grade-settings-bar]")); // nothing changed, no bar

        await page.InvokeAsync(() => page.FindComponent<MudNumericField<int>>().Instance.ValueChanged.InvokeAsync(3));
        page.WaitForElement("[data-testid=grade-settings-bar]");
        Assert.Empty(Config.SettingCalls); // nothing is sent before Save

        await page.Find("[data-testid=grade-settings-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=grade-settings-bar]")));
        var call = Assert.Single(Config.SettingCalls);
        Assert.Equal(("Grades.DecimalPlaces", "3"), (call.Name, call.Request.ValueJson));
    }

    [Fact]
    public async Task Discard_returns_to_the_saved_values_without_sending_anything()
    {
        var page = await RenderGroupAsync();
        page.WaitForElement("[data-testid=grade-setting-decimals]");
        await page.InvokeAsync(() => page.FindComponent<MudNumericField<int>>().Instance.ValueChanged.InvokeAsync(4));
        await page.Find("[data-testid=grade-settings-discard]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=grade-settings-bar]")));
        Assert.Empty(Config.SettingCalls);
    }

    [Fact]
    public async Task A_reader_without_update_cannot_change_the_values()
    {
        var page = await RenderGroupAsync("en", canEdit: false);
        page.WaitForElement("[data-testid=grade-setting-decimals]");

        Assert.True(page.FindComponent<MudNumericField<int>>().Instance.Disabled);
    }

    [Fact]
    public async Task Time_zone_picker_lists_iana_zones()
    {
        await PrepareAsync("en");
        var page = Render<ClientTimeZoneSetting>(p => p.Add(x => x.UnitId, Unit).Add(x => x.CanEdit, true));
        page.WaitForElement("[data-testid=client-timezone-select]");

        Assert.Contains("Asia/Riyadh", ClientTimeZoneSetting.KnownZones);
        Assert.All(ClientTimeZoneSetting.KnownZones, id => Assert.True(id == "UTC" || id.Contains('/'), id)); // IANA ids, not Windows display names

        await page.InvokeAsync(() => page.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync("Asia/Dubai"));
        await page.Find("[data-testid=client-timezone-save]").ClickAsync(new());

        var call = Assert.Single(Config.SettingCalls);
        Assert.Equal(("Org.TimeZone", "\"Asia/Dubai\""), (call.Name, call.Request.ValueJson));
    }

    [Fact]
    public async Task Grade_settings_render_in_arabic()
    {
        var page = await RenderGroupAsync("ar");
        page.WaitForElement("[data-testid=grade-setting-decimals]");

        Assert.Equal("الدرجات", page.Find("#grade-settings-heading").TextContent.Trim());
    }
}
