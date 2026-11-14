using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Lms.Client.Tests.Components.Messaging;

/// <summary>The third set of M6 parts (spec #615 §6, §7): channel preferences, push permission, and the moderation decision, status and context.</summary>
public sealed class MessagingComponents3Tests : ClientTestContext
{
    private static void English() => FakeCultureService.Apply(new CultureInfo("en"));

    private static IReadOnlyList<ChannelColumn> Columns(bool hasPhone = true) =>
    [
        new(NotificationChannel.InApp, "Always on"),
        new(NotificationChannel.Email, "huda@…"),
        new(NotificationChannel.Sms, "+966 5…", hasPhone, hasPhone ? null : "Add a mobile number in your profile", hasPhone ? null : "me/profile"),
        new(NotificationChannel.Push, "This device: off"),
    ];

    [Fact]
    public void In_app_is_always_on_and_locked_and_a_mandatory_cell_has_the_lock_before_its_switch()
    {
        English();
        var rows = new List<PreferenceRow>
        {
            new() { Key = "abs", Label = "Absence of my child", On = { [NotificationChannel.Email] = true }, Locked = { NotificationChannel.Email } },
        };
        var cut = Render<ChannelPreferences>(p => p.Add(c => c.Columns, Columns()).Add(c => c.Rows, rows));

        var inApp = cut.Find("[data-testid=prefs-abs-InApp]");
        Assert.NotEmpty(inApp.QuerySelectorAll(".lms-prefs-lock"));
        Assert.True(inApp.QuerySelector("input")!.HasAttribute("disabled"));
        var email = cut.Find("[data-testid=prefs-abs-Email]");
        Assert.NotEmpty(email.QuerySelectorAll(".lms-prefs-lock"));
        Assert.Contains("Set by your school", email.QuerySelector("input")!.GetAttribute("aria-label") ?? cut.Find("[data-testid=prefs-abs-Email] .mud-switch").InnerHtml);
        Assert.Contains("Set by your school", cut.Find(".lms-prefs-note").TextContent);
    }

    [Fact]
    public async Task An_open_switch_changes_the_row_and_tells_the_page()
    {
        English();
        var changed = 0;
        var row = new PreferenceRow { Key = "msg", Label = "New message" };
        var cut = Render<ChannelPreferences>(p => p.Add(c => c.Columns, Columns()).Add(c => c.Rows, new[] { row })
            .Add(c => c.OnChanged, EventCallback.Factory.Create(this, () => changed++)));

        await cut.Find("[data-testid=prefs-msg-Push] input").ChangeAsync(new ChangeEventArgs { Value = true });
        Assert.True(row.On[NotificationChannel.Push]);
        Assert.Equal(1, changed);
        Assert.Equal("Push", cut.Find("[data-testid=prefs-msg-Push]").GetAttribute("data-label"));
    }

    [Fact]
    public void Without_a_mobile_number_sms_is_disabled_and_says_why()
    {
        English();
        var cut = Render<ChannelPreferences>(p => p.Add(c => c.Columns, Columns(hasPhone: false)).Add(c => c.Rows, new[] { new PreferenceRow { Key = "msg", Label = "New message" } }));

        Assert.True(cut.Find("[data-testid=prefs-msg-Sms] input").HasAttribute("disabled"));
        Assert.Contains("Add a mobile number", cut.Find("[data-testid=prefs-col-Sms]").TextContent);
        Assert.Equal("me/profile", cut.Find("[data-testid=prefs-col-Sms] a").GetAttribute("href"));
    }

    [Fact]
    public void Push_off_offers_turn_on_denied_explains_without_a_button_and_on_shows_nothing()
    {
        English();
        var off = Render<PushPermissionAlert>(p => p.Add(c => c.State, PushState.Off));
        Assert.NotEmpty(off.FindAll("[data-testid=push-on]"));

        var denied = Render<PushPermissionAlert>(p => p.Add(c => c.State, PushState.Denied));
        Assert.Contains("site settings", denied.Markup);
        Assert.Empty(denied.FindAll("[data-testid=push-on]"));

        Assert.Empty(Render<PushPermissionAlert>(p => p.Add(c => c.State, PushState.On)).FindAll("[data-testid=push-alert]"));
    }

    [Fact]
    public void Decision_is_a_fieldset_of_six_options_in_order_with_the_severe_ones_last()
    {
        English();
        var cut = Render<ModerationDecision>(p => p.Add(c => c.SenderName, "Yousef"));

        Assert.Equal("Decision", cut.Find("fieldset legend").TextContent);
        var options = cut.FindAll("[data-testid^=mod-]").Where(e => e.TagName == "LABEL").Select(e => e.GetAttribute("data-testid")).ToList();
        Assert.Equal(new[] { "mod-Dismiss", "mod-Remove", "mod-Warn", "mod-Restrict", "mod-RemoveAndRestrict", "mod-LockConversation" }, options);
        Assert.Equal(3, cut.FindAll(".lms-mod-severe").Count);
        Assert.Contains("Yousef gets a warning notice", cut.Find("[data-testid=mod-Warn]").TextContent);
        Assert.Contains("not a block between people", cut.Find("[data-testid=mod-Restrict]").TextContent);
    }

    [Fact]
    public async Task Choosing_restrict_asks_for_how_long()
    {
        English();
        ModerationAction? chosen = null;
        var cut = Render<ModerationDecision>(p => p.Add(c => c.SenderName, "Yousef")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<ModerationAction?>(this, a => chosen = a)));

        Assert.Empty(cut.FindAll("[data-testid=mod-days]"));
        await cut.Find("[data-testid=mod-Restrict] input").ChangeAsync(new ChangeEventArgs { Value = "on" });
        Assert.Equal(ModerationAction.Restrict, chosen);
        Assert.NotEmpty(cut.FindAll("[data-testid=mod-days]"));
    }

    [Theory]
    [InlineData(ReportStatus.New, false, "New", "mud-chip-color-info")]
    [InlineData(ReportStatus.New, true, "New", "mud-chip-color-error")]
    [InlineData(ReportStatus.InReview, false, "In review", "mud-chip-color-warning")]
    [InlineData(ReportStatus.Kept, false, "Kept", "mud-chip-color-default")]
    public void Report_status_chip_has_its_word_and_colour(ReportStatus status, bool safety, string word, string colour)
    {
        English();
        var cut = Render<ReportStatusChip>(p => p.Add(c => c.Status, status).Add(c => c.SafetyConcern, safety));
        var chip = cut.Find(".mud-chip");
        Assert.Contains(word, chip.TextContent);
        Assert.Contains(colour, chip.ClassName);
    }

    [Fact]
    public void Reported_message_is_outlined_and_labelled_in_words()
    {
        English();
        var cut = Render<ReportedMessage>(p => p.Add(c => c.ChildContent, (RenderFragment)(b => b.AddContent(0, "the message"))));
        Assert.Contains("Reported message", cut.Find(".lms-flagged").TextContent);
        Assert.Contains("the message", cut.Markup);
    }
}
