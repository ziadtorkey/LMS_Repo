using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Lms.Client.Pages.Announcements;
using Lms.Client.Pages.Moderation;
using Lms.Client.Pages.Notifications;
using Lms.Client.Pages.Settings;
using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Moderation;

/// <summary>
/// The FE2 pages of M6 (announcements, preferences, rules, communication settings, moderation) against the checklist of #615 §9 that a unit test can see: every control has an
/// accessible name, tables stack below 600 px with labelled cells, the context log is a log, and the pages render in both languages.
/// </summary>
public sealed class M6Fe2AccessibilityTests : ClientTestContext
{
    private static readonly string[] All =
    [
        Permissions.Announcements.View, Permissions.Announcements.Manage, Permissions.OrgUnits.View, Permissions.Comms.Manage, Permissions.Messaging.Moderate, Permissions.Forums.View,
    ];

    private readonly FakeModerationApi _moderation = new();
    private readonly FakeAnnouncementsApi _announcements = new();
    private readonly FakeMessagingRules _rules = new();

    public M6Fe2AccessibilityTests()
    {
        Services.AddSingleton<IModerationApi>(_moderation);
        Services.AddSingleton<IAnnouncementsApi>(_announcements);
        Services.AddSingleton<ICommsApi>(new FakeCommsApi(_rules));
        Services.AddSingleton<IPushDevice>(new FakePushDevice());
        Services.AddSingleton<IClassesApi>(new FakeClassesApi());
        Services.AddSingleton<ILessonPlansApi>(new FakeLessonPlansApi());
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
        Services.AddSingleton<TimeProvider>(TimeProvider.System);
    }

    private async Task PrepareAsync(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(All);
        Api.Me = Api.Me with { Contexts = [new(Context.ActiveAssignmentId!.Value, Guid.NewGuid(), "م", "Org", FakeMessagingApi.UnitA, "و", "Unit", "مدير", "Manager")] };
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        _moderation.AddReport(withFile: true);
        await _announcements.CreateAsync(new(FakeMessagingApi.UnitA, "إعلان", "Announcement", "نص", "Body", [new(Lms.Shared.Messaging.AnnouncementTargetKinds.Organization, null, true)], null, null, null, true, true, null, true));
    }

    private static string Name(IElement e) =>
        FirstNonBlank(e.GetAttribute("aria-label"), e.GetAttribute("title"), e.GetAttribute("aria-labelledby") is { Length: > 0 } ? "labelled" : null, e.TextContent.Trim(),
            e.QuerySelector("[aria-label]")?.GetAttribute("aria-label"), e.QuerySelector("img[alt]")?.GetAttribute("alt")) ?? string.Empty;

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>The unnamed controls of a rendered page: buttons and links with no name, inputs with no label, images with no alt.</summary>
    private static List<string> Problems(IRenderedComponent<IComponent> page)
    {
        var problems = new List<string>();
        foreach (var b in page.FindAll("button, a[href], [role=button]"))
        {
            if (string.IsNullOrWhiteSpace(Name(b))) problems.Add("no name: " + b.OuterHtml[..Math.Min(120, b.OuterHtml.Length)]);
        }

        foreach (var i in page.FindAll("input:not([type=hidden]), textarea, select"))
        {
            var id = i.GetAttribute("id");
            var labelled = !string.IsNullOrWhiteSpace(i.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(i.GetAttribute("aria-labelledby"))
                || (id is { Length: > 0 } && page.FindAll($"label[for=\"{id}\"]").Count > 0) || i.Closest("label") is not null
                || i.Closest(".mud-input-control, .mud-checkbox, .mud-switch, .mud-radio")?.QuerySelector("label, .mud-input-label, .mud-typography") is not null;
            if (!labelled) problems.Add("no label: " + i.OuterHtml[..Math.Min(120, i.OuterHtml.Length)]);
        }

        foreach (var img in page.FindAll("img:not([alt])")) problems.Add("no alt: " + img.OuterHtml);
        return problems;
    }

    private static void StackedTablesAreLabelled(IRenderedComponent<IComponent> page)
    {
        foreach (var table in page.FindAll(".lms-stack-table"))
        {
            foreach (var td in table.QuerySelectorAll("tbody td"))
            {
                Assert.False(string.IsNullOrWhiteSpace(td.GetAttribute("data-label")), "a stacked cell has no data-label: " + td.OuterHtml[..Math.Min(100, td.OuterHtml.Length)]);
            }
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Moderation_page_has_no_unnamed_controls_and_a_labelled_stacked_table(string culture)
    {
        await PrepareAsync(culture);
        var page = Render<ModerationQueuePage>();
        page.WaitForElement("[data-testid=mod-table]");
        await page.Find("[data-testid^=mod-open-]").ClickAsync(new());
        page.WaitForElement("[data-testid=mod-context]");

        Assert.Empty(Problems(page));
        StackedTablesAreLabelled(page);
        Assert.NotNull(page.Find("[data-testid=mod-context][role=log]")); // the frozen context is a log
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Announcement_pages_have_no_unnamed_controls_and_a_labelled_stacked_table(string culture)
    {
        await PrepareAsync(culture);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", "manage"));
        var list = Render<OrgAnnouncementsPage>();
        list.WaitForElement("[data-testid=anns-table]");

        Assert.Empty(Problems(list));
        StackedTablesAreLabelled(list);
        await DisposeComponentsAsync();

        await PrepareAsync(culture);
        var composer = Render<AnnouncementComposerPage>();
        composer.WaitForElement("[data-testid=ann-compose]");
        Assert.Empty(Problems(composer));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Settings_pages_have_no_unnamed_controls(string culture)
    {
        await PrepareAsync(culture);
        var rules = Render<MessagingRulesPage>();
        rules.WaitForElement("[data-testid=rules-matrix]");
        Assert.Empty(Problems(rules));
        StackedTablesAreLabelled(rules);
        await DisposeComponentsAsync();

        await PrepareAsync(culture);
        var comms = Render<CommunicationSettingsPage>();
        comms.WaitForElement("[data-testid=comms-channels]");
        Assert.Empty(Problems(comms));
        await DisposeComponentsAsync();

        await PrepareAsync(culture);
        var prefs = Render<NotificationPreferencesPage>();
        prefs.WaitForElement("[data-testid=prefs]");
        Assert.Empty(Problems(prefs));
        StackedTablesAreLabelled(prefs);
    }

    [Fact]
    public void The_audience_options_wrap_below_600px_so_no_label_is_cut_off()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var css = File.ReadAllText(Path.Combine(dir!, "src", "Lms.Client", "wwwroot", "css", "app.css"));

        var rule = css[css.IndexOf(".lms-ann-scopes { flex-direction", StringComparison.Ordinal)..];
        Assert.Contains("flex-direction: column", rule);
        Assert.Contains("white-space: normal", rule);
    }
}
