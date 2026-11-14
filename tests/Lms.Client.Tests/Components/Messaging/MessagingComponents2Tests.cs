using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Lms.Client.Tests.Components.Messaging;

/// <summary>The second set of M6 parts (spec #615 §2.5, §3, §4.4, §5, §7a): notifications, the D69-aware picker rows, the thread header, the rules
/// cell and the announcement composer.</summary>
public sealed class MessagingComponents2Tests : ClientTestContext
{
    private static void English() => FakeCultureService.Apply(new CultureInfo("en"));

    [Fact]
    public void Unread_notification_says_so_first_in_its_accessible_name()
    {
        English();
        var cut = Render<NotificationItem>(p => p.Add(c => c.Href, "messages/1").Add(c => c.Text, "Huda Al-Harbi sent you a message")
            .Add(c => c.Quote, "Thank you").Add(c => c.TimeText, "2 min").Add(c => c.Unread, true)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<b><bdi>Huda Al-Harbi</bdi></b> sent you a message"))));

        var link = cut.Find("a.lms-notif");
        Assert.Contains("unread", link.ClassList);
        Assert.Equal("Unread: Huda Al-Harbi sent you a message, Thank you, 2 min", link.GetAttribute("aria-label"));
        Assert.Equal("Huda Al-Harbi", cut.Find(".lms-notif-text b bdi").TextContent);
    }

    [Fact]
    public async Task Panel_marks_all_read_only_when_something_is_unread_and_links_to_settings_and_the_list()
    {
        English();
        var marked = 0;
        var none = Render<NotificationPanel>(p => p.Add(c => c.HasUnread, false));
        Assert.True(none.Find("[data-testid=notif-mark-all]").HasAttribute("disabled"));

        var cut = Render<NotificationPanel>(p => p.Add(c => c.HasUnread, true).Add(c => c.OnMarkAllRead, EventCallback.Factory.Create(this, () => marked++)));
        await cut.Find("[data-testid=notif-mark-all]").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, marked);
        Assert.Equal("Notifications", cut.Find("h2.lms-notif-panel-title").TextContent);
        Assert.Equal("notifications/preferences", cut.Find("[data-testid=notif-settings]").GetAttribute("href"));
        Assert.Equal("notifications", cut.Find("[data-testid=notif-all]").GetAttribute("href"));
    }

    [Fact]
    public void Empty_panel_shows_the_empty_state()
    {
        English();
        var cut = Render<NotificationPanel>(p => p.Add(c => c.IsEmpty, true));
        Assert.NotEmpty(cut.FindAll("[data-testid=notif-empty]"));
    }

    [Fact]
    public void Disabled_recipient_gives_its_reason_and_never_a_size()
    {
        English();
        var cut = Render<RecipientOption>(p => p.Add(c => c.Name, "Physics 1 · 10-B — guardians").Add(c => c.Icon, MudBlazor.Icons.Material.Rounded.Block)
            .Add(c => c.SizeText, "31 people").Add(c => c.Disabled, true).Add(c => c.DisabledReason, "Not allowed: you don't teach 10-B"));

        var opt = cut.Find(".lms-picker-opt");
        Assert.Equal("true", opt.GetAttribute("aria-disabled"));
        Assert.Equal("Physics 1 · 10-B — guardians, Not allowed: you don't teach 10-B", opt.GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(".lms-picker-size"));
    }

    [Fact]
    public void Selected_person_has_a_check_and_its_caption()
    {
        English();
        var cut = Render<RecipientOption>(p => p.Add(c => c.Name, "Huda Al-Harbi").Add(c => c.Caption, "Guardian of Omar Saleh · 10-A").Add(c => c.Selected, true));

        Assert.Equal("true", cut.Find(".lms-picker-opt").GetAttribute("aria-selected"));
        Assert.NotEmpty(cut.FindAll(".lms-picker-check svg"));
        Assert.Contains("Guardian of Omar Saleh", cut.Find(".lms-picker-caption").TextContent);
    }

    [Fact]
    public void Safeguarding_row_explains_d69_and_is_not_selectable()
    {
        English();
        var cut = Render<SafeguardingOption>();

        var opt = cut.Find("[data-testid=picker-d69]");
        Assert.Equal("true", opt.GetAttribute("aria-disabled"));
        Assert.Contains("No students here", opt.TextContent);
        Assert.Contains("not a private message", opt.TextContent);
    }

    [Fact]
    public async Task Group_header_opens_members_and_person_header_shows_the_caption()
    {
        English();
        var opened = false;
        var group = Render<ThreadHeader>(p => p.Add(c => c.Name, "Physics 1 · 10-A — guardians").Add(c => c.IsGroup, true).Add(c => c.MemberCount, 24)
            .Add(c => c.OnMembers, EventCallback.Factory.Create(this, () => opened = true)));
        Assert.Contains("Group · 24 people", group.Find("[data-testid=thread-members]").TextContent);
        await group.Find("[data-testid=thread-members]").ClickAsync(new MouseEventArgs());
        Assert.True(opened);
        Assert.Empty(group.FindAll("[data-testid=thread-back]"));

        var person = Render<ThreadHeader>(p => p.Add(c => c.Name, "Huda Al-Harbi").Add(c => c.Caption, "Guardian of Omar Saleh · 10-A")
            .Add(c => c.OnBack, EventCallback.Factory.Create(this, () => { })));
        Assert.Contains("Guardian of Omar Saleh", person.Find(".lms-thread-caption").TextContent);
        Assert.Equal("Back to messages", person.Find("[data-testid=thread-back]").GetAttribute("aria-label"));
    }

    [Fact]
    public void Locked_rule_cell_is_a_chip_with_the_safeguarding_reason_and_no_select()
    {
        English();
        var cut = Render<MessagingRuleCell>(p => p.Add(c => c.Via, MessagingVia.GroupsOnly).Add(c => c.Locked, true)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<select data-testid=\"rule-select\"></select>"))));

        Assert.Contains("Groups only", cut.Markup);
        Assert.Contains("Set by the platform (safeguarding)", cut.Find(".lms-visually-hidden").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=rule-select]"));
    }

    [Fact]
    public void Editable_rule_cell_shows_the_page_select()
    {
        English();
        var cut = Render<MessagingRuleCell>(p => p.Add(c => c.Via, MessagingVia.SameUnit)
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<select data-testid=\"rule-select\"></select>"))));
        Assert.NotEmpty(cut.FindAll("[data-testid=rule-select]"));
    }

    [Theory]
    [InlineData(37, 37)]
    [InlineData(100, 100)]
    [InlineData(590, 600)]
    [InlineData(612, 600)]
    [InlineData(1249, 1200)]
    public void Reach_is_rounded_above_100(int count, int shown) => Assert.Equal(shown, ReachEstimate.Rounded(count));

    [Fact]
    public void Reach_is_a_status_and_says_it_counts_only_the_authors_scope()
    {
        English();
        var cut = Render<ReachEstimate>(p => p.Add(c => c.Count, 590));
        var status = cut.Find("[role=status]");
        Assert.Contains("About 600 people", status.TextContent);
        Assert.Contains("only in the units you can see", status.TextContent);
    }

    [Fact]
    public async Task Role_chips_toggle_with_aria_pressed()
    {
        English();
        var roles = new HashSet<AudienceRole> { AudienceRole.Guardians };
        var cut = Render<RoleFilterChips>(p => p.Add(c => c.Selected, roles));

        Assert.Equal("true", cut.Find("[data-testid=role-Guardians]").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid=role-Staff]").GetAttribute("aria-pressed"));
        await cut.Find("[data-testid=role-Staff]").ClickAsync(new MouseEventArgs());
        Assert.Contains(AudienceRole.Staff, roles);
    }

    [Fact]
    public async Task Publishing_needs_both_titles_a_body_targets_and_a_role_and_says_why_at_each_field()
    {
        English();
        var published = 0;
        var draft = new AnnouncementDraft { TitleAr = "لقاء", Scope = AudienceScopeKind.ClassCourse };
        draft.Roles.Clear();
        var cut = Render<AnnouncementComposerForm>(p => p.Add(c => c.Draft, draft)
            .Add(c => c.AllowedScopes, new[] { AudienceScopeKind.ClassCourse, AudienceScopeKind.Group })
            .Add(c => c.OnPublish, EventCallback.Factory.Create<AnnouncementDraft>(this, _ => published++)));

        await cut.Find("[data-testid=ann-publish]").ClickAsync(new MouseEventArgs());

        Assert.Equal(0, published);
        Assert.Contains("Enter the title in English too", cut.Markup);
        Assert.Contains("Write the text in at least one language.", cut.Markup);
        Assert.NotEmpty(cut.FindAll("[data-testid=ann-targets-error]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=role-error]"));
    }

    [Fact]
    public async Task A_draft_needs_only_both_titles()
    {
        English();
        AnnouncementDraft? saved = null;
        var draft = new AnnouncementDraft { TitleAr = "لقاء", TitleEn = "Meeting" };
        var cut = Render<AnnouncementComposerForm>(p => p.Add(c => c.Draft, draft).Add(c => c.AllowedScopes, new[] { AudienceScopeKind.ClassCourse })
            .Add(c => c.OnSaveDraft, EventCallback.Factory.Create<AnnouncementDraft>(this, d => saved = d)));

        await cut.Find("[data-testid=ann-save-draft]").ClickAsync(new MouseEventArgs());
        Assert.Same(draft, saved);
    }

    [Fact]
    public async Task A_complete_announcement_publishes_and_a_schedule_must_be_in_the_future()
    {
        English();
        var published = 0;
        var draft = new AnnouncementDraft { TitleAr = "لقاء", TitleEn = "Meeting", BodyEn = "Thursday at 6.", Mode = PublishMode.Schedule,
            PublishAtLocal = new DateTime(2026, 11, 13, 9, 0, 0) };
        draft.TargetIds.Add(Guid.NewGuid());
        var cut = Render<AnnouncementComposerForm>(p => p.Add(c => c.Draft, draft).Add(c => c.AllowedScopes, new[] { AudienceScopeKind.ClassCourse })
            .Add(c => c.NowLocal, new DateTime(2026, 11, 14, 9, 0, 0)).Add(c => c.TimeZoneId, "Asia/Riyadh")
            .Add(c => c.OnPublish, EventCallback.Factory.Create<AnnouncementDraft>(this, _ => published++)));

        Assert.Contains("Schedule", cut.Find("[data-testid=ann-publish]").TextContent);
        Assert.Contains("Asia/Riyadh", cut.Find("[data-testid=ann-tz]").TextContent);
        await cut.Find("[data-testid=ann-publish]").ClickAsync(new MouseEventArgs());
        Assert.Equal(0, published);
        Assert.Contains("Choose a date and time in the future.", cut.Markup);

        draft.PublishAtLocal = new DateTime(2026, 11, 15, 7, 0, 0);
        await cut.Find("[data-testid=ann-publish]").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, published);
    }

    [Fact]
    public void Only_the_scope_kinds_the_author_may_use_are_offered_and_units_get_include_below()
    {
        English();
        var draft = new AnnouncementDraft { Scope = AudienceScopeKind.Unit };
        var cut = Render<AnnouncementComposerForm>(p => p.Add(c => c.Draft, draft)
            .Add(c => c.AllowedScopes, new[] { AudienceScopeKind.Organization, AudienceScopeKind.Unit, AudienceScopeKind.Level }));

        Assert.NotEmpty(cut.FindAll("[data-testid=ann-scope-Unit]"));
        Assert.Empty(cut.FindAll("[data-testid=ann-scope-ClassCourse]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=ann-subtree]"));
    }

    [Fact]
    public void Toggle_row_switch_is_labelled_and_described_by_its_text()
    {
        English();
        var cut = Render<ToggleRow>(p => p.Add(c => c.Title, "Pin to the top").Add(c => c.Help, "Pinned announcements stay first."));

        var input = cut.Find("input");
        var labelledBy = input.GetAttribute("aria-labelledby") ?? cut.Find(".mud-switch").GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrEmpty(labelledBy));
        Assert.Equal("Pin to the top", cut.Find($"#{labelledBy}").TextContent);
    }
}
