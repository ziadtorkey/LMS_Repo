using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Pages.Notifications;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Messaging;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Notifications;

/// <summary>Channel preferences and push on the preferences page (M6 Task 26, FR-NOT-01, FR-NOT-03, D66, D76) against the comms fakes.</summary>
public sealed class ChannelPreferencesTests : ClientTestContext
{
    private readonly FakeCommsApi _comms = new();
    private readonly FakePushDevice _push = new();
    private readonly string _event = NotificationEvents.All.First(e => !e.Mandatory).Key;
    private readonly string _mandatory = NotificationEvents.All.First(e => e.Mandatory).Key;

    public ChannelPreferencesTests()
    {
        Services.AddSingleton<ICommsApi>(_comms);
        Services.AddSingleton<IPushDevice>(_push);
    }

    private IRenderedComponent<NotificationPreferencesPage> Open(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<NotificationPreferencesPage>();
        page.WaitForElement("[data-testid=prefs]");
        return page;
    }

    private static string Cell(string key, string channel) => $"[data-testid=\"prefs-{key}-{channel}\"] input";

    [Fact]
    public async Task Effective_value_and_source_are_shown()
    {
        await _comms.SavePreferencesAsync(new SavePreferencesRequest([new SavePreferenceRequest(_event, NotificationChannels.Email, true)]));

        var page = Open();

        Assert.True(page.Find(Cell(_event, "Email")).HasAttribute("checked")); // the effective value
        Assert.Contains("set by you", page.Find($"[data-testid=\"prefs-source-{_event}-Email\"]").TextContent); // and where it comes from
        Assert.Contains("required by your school", page.Find($"[data-testid=\"prefs-source-{_mandatory}-InApp\"]").TextContent);
    }

    [Fact]
    public void Mandatory_events_cannot_be_switched_off()
    {
        var page = Open();

        Assert.True(page.Find(Cell(_mandatory, "InApp")).HasAttribute("disabled"));
        Assert.NotEmpty(page.FindAll($"[data-testid=\"prefs-{_mandatory}-InApp\"] .lms-prefs-lock"));
    }

    [Fact]
    public void Channel_cells_are_disabled_with_a_reason_when_off_or_no_contact()
    {
        _comms.Channels[1] = new ContactChannelDto(NotificationChannels.Email, ChannelStates.NeedsSetup, null, false);
        _push.State = PushState.Off;

        var page = Open();

        Assert.True(page.Find(Cell(_event, "Email")).HasAttribute("disabled"));
        Assert.Contains("No email address", page.Find("[data-testid=prefs-col-Email]").TextContent);
        Assert.True(page.Find(Cell(_event, "Push")).HasAttribute("disabled"));
        Assert.Contains("Turn on push for this browser first", page.Find("[data-testid=prefs-col-Push]").TextContent);
    }

    [Fact]
    public void No_sms_column_for_any_user()
    {
        var page = Open();

        Assert.Empty(page.FindAll("[data-testid=prefs-col-Sms]"));
        Assert.DoesNotContain("SMS", page.Markup, StringComparison.OrdinalIgnoreCase); // D76
    }

    [Fact]
    public void Email_bounce_state_is_shown()
    {
        _comms.Channels[1] = new ContactChannelDto(NotificationChannels.Email, ChannelStates.Bounced, "s***@example.com", true);

        var page = Open();

        Assert.Contains("bouncing", page.Find("[data-testid=prefs-email-bounced]").TextContent);
        Assert.True(page.Find("[data-testid=prefs-test-email]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Test_email_rate_limit_is_explained()
    {
        var page = Open();

        for (var i = 0; i < MessagingLimits.EmailTestsPerDay; i++) await page.Find("[data-testid=prefs-test-email]").ClickAsync(new());
        Assert.Contains("sent", page.Find("[data-testid=prefs-test-result]").TextContent);
        await page.Find("[data-testid=prefs-test-email]").ClickAsync(new());

        Assert.Contains("3 test emails a day", page.Find("[data-testid=prefs-test-result]").TextContent);
    }

    [Fact]
    public async Task Unsaved_changes_are_flagged_and_only_the_changed_switches_are_saved()
    {
        var page = Open();
        Assert.Empty(page.FindAll("[data-testid=prefs-bar]"));

        await page.Find(Cell(_event, "Email")).ChangeAsync(new() { Value = true });

        page.WaitForElement("[data-testid=prefs-bar]");
        Assert.Null(_comms.LastPreferences);
        await page.Find("[data-testid=prefs-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=prefs-bar]")));
        var sent = Assert.Single(_comms.LastPreferences!.Preferences);
        Assert.Equal((_event, NotificationChannels.Email, true), (sent.EventKey, sent.Channel, sent.Enabled));
    }

    [Fact]
    public void First_push_opt_in_shows_and_records_the_consent_sentence()
    {
        var page = Open();
        Assert.Empty(page.FindAll("[data-testid=push-consent]"));

        page.Find("[data-testid=push-on]").Click();

        Assert.Contains("never message text", page.WaitForElement("[data-testid=push-consent]").TextContent);
        Assert.False(_push.Consent); // nothing is recorded until the person agrees
        page.Find("[data-testid=push-consent-agree]").Click();

        page.WaitForAssertion(() => Assert.True(_push.Consent));
    }

    [Fact]
    public async Task Push_permission_is_requested_only_after_a_click()
    {
        var page = Open();
        Assert.Equal(0, _push.PermissionPrompts); // opening the page never asks

        await page.Find("[data-testid=push-on]").ClickAsync(new());
        Assert.Equal(0, _push.PermissionPrompts); // the consent sentence comes first
        await page.Find("[data-testid=push-consent-agree]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(1, _push.PermissionPrompts));
        Assert.Single(await _comms.GetPushSubscriptionsAsync()); // and the subscription went to the server
    }

    [Fact]
    public void A_later_opt_in_skips_the_consent_sentence_because_it_is_already_recorded()
    {
        _push.Consent = true;
        var page = Open();

        page.Find("[data-testid=push-on]").Click();

        page.WaitForAssertion(() => Assert.Equal(1, _push.PermissionPrompts));
        Assert.Empty(page.FindAll("[data-testid=push-consent]"));
    }

    [Fact]
    public void Push_blocked_state_explains_how_to_unblock()
    {
        _push.State = PushState.Denied;

        var page = Open();

        Assert.Contains("site settings", page.Find("[data-testid=push-alert]").TextContent);
        Assert.Empty(page.FindAll("[data-testid=push-on]")); // a button would do nothing
    }

    [Fact]
    public void Push_unsupported_state_is_explained()
    {
        _push.State = PushState.Unsupported;

        var page = Open();

        Assert.Contains("Not available on this browser", page.Find("[data-testid=push-unsupported]").TextContent);
    }

    [Fact]
    public async Task A_push_device_is_listed_and_removed()
    {
        var sub = await _comms.AddPushSubscriptionAsync(new SavePushSubscriptionRequest("https://push.example/a", "k", "a", "Work laptop"));
        var page = Open();
        Assert.Contains("Work laptop", page.Find($"[data-testid=prefs-device-{sub.Id}]").TextContent);

        await page.Find($"[data-testid=prefs-device-remove-{sub.Id}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid=prefs-device-{sub.Id}]")));
    }

    [Fact]
    public void Page_renders_rtl_in_arabic()
    {
        var page = Open("ar");

        Assert.Contains("تفضيلات الإشعارات", page.Markup);
        Assert.Contains("تعمل دائمًا", page.Find("[data-testid=prefs-col-InApp]").TextContent);
    }
}
