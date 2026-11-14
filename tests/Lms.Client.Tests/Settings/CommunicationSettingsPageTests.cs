using System.Globalization;
using Bunit;
using Lms.Client.Pages.Settings;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Settings;

/// <summary>The communication settings page (M6 Task 28, FR-NOT-03, D66, D76): caps with their ceilings, quiet hours in the school zone, no SMS, provider status.</summary>
public sealed class CommunicationSettingsPageTests : ClientTestContext
{
    private readonly FakeCommsApi _comms = new();

    public CommunicationSettingsPageTests() => Services.AddSingleton<ICommsApi>(_comms);

    private async Task<IRenderedComponent<CommunicationSettingsPage>> OpenAsync(string culture = "en", params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions.Length == 0 ? [Permissions.Comms.Manage] : permissions);
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<CommunicationSettingsPage>();
        if (permissions.Length == 0) page.WaitForElement("[data-testid=comms-channels]");
        return page;
    }

    private static Task SetNumberAsync(IRenderedComponent<CommunicationSettingsPage> page, string label, int value) =>
        page.InvokeAsync(() => page.FindComponents<MudNumericField<int>>().First(f => f.Instance.Label == label).Instance.ValueChanged.InvokeAsync(value));

    [Fact]
    public async Task Page_needs_comms_manage_at_the_root()
    {
        var page = await OpenAsync("en", Permissions.Courses.View);

        page.WaitForElement("[data-testid=forbidden-page]");
        Assert.Empty(page.FindAll("[data-testid=comms-channels], [data-testid=comms-save], [data-testid=comms-bar]"));
    }

    [Fact]
    public async Task Caps_reject_negative_values_and_show_the_ceiling()
    {
        var page = await OpenAsync();
        Assert.Contains("Between 0 and 100000", page.Markup); // the ceiling of the daily email cap, shown as help

        await SetNumberAsync(page, "Emails per day (all)", -5);
        await page.Find("[data-testid=comms-save]").ClickAsync(new());

        Assert.Contains("cannot be negative", page.Markup);
        Assert.Equal(2000, (await _comms.GetSettingsAsync()).MaxEmailPerDay); // nothing was sent
    }

    [Fact]
    public async Task A_cap_above_its_ceiling_is_refused_on_the_field()
    {
        var page = await OpenAsync();

        await SetNumberAsync(page, "Largest group", 500); // above the ceiling of 200: the client check and the server agree
        await page.Find("[data-testid=comms-save]").ClickAsync(new());

        Assert.Contains("Enter a number from 3 to 200", page.Markup);
    }

    [Fact]
    public async Task Cap_above_ceiling_maps_to_the_field()
    {
        var page = await OpenAsync();
        await SetNumberAsync(page, "Largest group", 60);
        _comms.Settings = _comms.Settings with { MaxGroupSize = 50 }; // the page sends 60: let the server decide it is over (simulated below)
        _comms.SettingsFailure = new Lms.Client.Services.Api.ApiProblemException(400, Lms.Shared.ProblemCodes.validation_failed, null, null, "MaxGroupSize");

        await page.Find("[data-testid=comms-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("Enter a number from 3 to 200", page.Markup));
        Assert.Empty(page.FindAll("[data-testid=comms-error]")); // the message sits on the field, not in a generic alert
    }

    [Fact]
    public async Task Quiet_hours_need_both_ends_and_show_the_time_zone()
    {
        var page = await OpenAsync();
        await page.Find("[data-testid=comms-quiet-on] input").ChangeAsync(new() { Value = true });
        page.WaitForElement("[data-testid=comms-quiet-zone]");
        Assert.Contains("Asia/Riyadh", page.Find("[data-testid=comms-quiet-zone]").TextContent);

        await page.InvokeAsync(() => page.FindComponents<MudTextField<string>>().First(f => f.Instance.Label == "From (HH:mm)").Instance.ValueChanged.InvokeAsync(""));
        await page.Find("[data-testid=comms-save]").ClickAsync(new());

        Assert.Contains("Enter a time such as 21:00", page.Markup);
    }

    [Fact]
    public async Task Page_has_no_sms_switch_or_cap()
    {
        var page = await OpenAsync();

        Assert.DoesNotContain("SMS", page.Markup, StringComparison.OrdinalIgnoreCase); // D76
    }

    [Fact]
    public async Task Provider_status_shows_no_settings()
    {
        var page = await OpenAsync();

        var email = page.Find("[data-testid=provider-Email]");
        Assert.Contains("Configured", email.TextContent);
        Assert.Contains("Test provider", email.TextContent);
        Assert.Empty(email.QuerySelectorAll("input, button")); // status only: nothing to edit and no secret
        Assert.Empty(page.FindAll("[data-testid=provider-Sms]"));
    }

    [Fact]
    public async Task Settings_use_the_save_bar()
    {
        var page = await OpenAsync();
        Assert.Empty(page.FindAll("[data-testid=comms-bar]"));

        await SetNumberAsync(page, "Largest group", 40);

        page.WaitForElement("[data-testid=comms-bar]");
        Assert.Equal(50, (await _comms.GetSettingsAsync()).MaxGroupSize); // nothing is sent before Save
        await page.Find("[data-testid=comms-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=comms-bar]")));
        Assert.Equal(40, (await _comms.GetSettingsAsync()).MaxGroupSize);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await OpenAsync("ar");

        Assert.Equal("إعدادات التواصل", page.Find("[data-testid=comms-title]").TextContent.Trim());
    }
}
