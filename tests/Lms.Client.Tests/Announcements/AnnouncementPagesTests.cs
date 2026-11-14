using System.Globalization;
using Bunit;
using Lms.Client.Components.Messaging;
using Lms.Client.Pages.Announcements;
using Lms.Client.Services.Api;
using Lms.Client.Services.Messaging;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Announcements;

/// <summary>The announcement pages (M6 Task 25, FR-MSG-03, D66, D76) on the fakes: compose, schedule by the server clock, read, manage.</summary>
public sealed class AnnouncementPagesTests : ClientTestContext
{
    private static readonly DateTime Now = new(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
    private static readonly string[] Manager = [Permissions.Announcements.View, Permissions.Announcements.Manage, Permissions.OrgUnits.View];
    private static readonly string[] Teacher = [Permissions.Announcements.View, Permissions.Announcements.Manage];

    private readonly FakeAnnouncementsApi _api = new() { Now = () => Now };

    public AnnouncementPagesTests()
    {
        Services.AddSingleton<IAnnouncementsApi>(_api);
        Services.AddSingleton<IClassesApi>(new FakeClassesApi());
        Services.AddSingleton<ILessonPlansApi>(new FakeLessonPlansApi());
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
        Services.AddSingleton<TimeProvider>(new FixedClock());
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private async Task PrepareAsync(string[] permissions, string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions);
        Api.Me = Api.Me with
        {
            Contexts = [new(Context.ActiveAssignmentId!.Value, Guid.NewGuid(), "م", "Org", FakeMessagingApi.UnitA, "و", "Unit", "مدير", "Manager")],
        };
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }

    private async Task<IRenderedComponent<AnnouncementComposerPage>> ComposerAsync(string[] permissions, string culture = "en")
    {
        await PrepareAsync(permissions, culture);
        var page = Render<AnnouncementComposerPage>();
        page.WaitForElement("[data-testid=ann-compose]");
        return page;
    }

    private static Task SetAsync(IRenderedComponent<AnnouncementComposerPage> page, string testId, string value) =>
        page.Find($"[data-testid={testId}] input, [data-testid={testId}] textarea").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = value });

    [Fact]
    public async Task Composer_requires_both_titles_and_an_audience()
    {
        var page = await ComposerAsync(Manager);

        await page.Find("[data-testid=ann-publish]").ClickAsync(new());

        Assert.Contains("title in Arabic", page.Markup);
        Assert.Contains("title in English", page.Markup);
        Assert.NotNull(page.Find("[data-testid=ann-targets-error]")); // a unit scope with no unit chosen
        Assert.Empty(await _api.ListAsync(new AnnouncementQuery()).ContinueWith(t => t.Result.Items)); // nothing was sent
    }

    [Fact]
    public async Task Reach_preview_shows_count_per_chip()
    {
        var page = await ComposerAsync(Manager);
        var picker = page.FindComponent<AudiencePicker>();
        var unit = FakeUnits.All[0].Id;

        await page.InvokeAsync(() => picker.Instance.SelectedChanged.InvokeAsync([unit]));
        page.WaitForAssertion(() => Assert.Contains("550", page.Find("[data-testid=ann-audience]").TextContent), TimeSpan.FromSeconds(5)); // students 240 + guardians 310

        await page.Find("[data-testid=role-Guardians]").ClickAsync(new()); // switch guardians off

        page.WaitForAssertion(() => Assert.Contains("250", page.Find("[data-testid=ann-audience]").TextContent), TimeSpan.FromSeconds(5)); // 240, rounded to 50 above 100
    }

    [Fact]
    public async Task Composer_offers_no_sms_option()
    {
        var page = await ComposerAsync(Manager);

        Assert.DoesNotContain("SMS", page.Markup, StringComparison.OrdinalIgnoreCase); // D76
    }

    [Fact]
    public async Task Teacher_audience_options_are_own_courses_only_and_there_is_no_urgent_switch()
    {
        var page = await ComposerAsync(Teacher);

        Assert.Empty(page.FindAll("[data-testid=ann-scope-Organization], [data-testid=ann-scope-Unit], [data-testid=ann-scope-Class]"));
        Assert.NotNull(page.Find("[data-testid=ann-scope-ClassCourse]"));
        Assert.DoesNotContain("urgent", page.Markup, StringComparison.OrdinalIgnoreCase); // the urgent switch belongs to unit-level authors
    }

    [Fact]
    public async Task Guardians_chip_default_depends_on_the_author()
    {
        var manager = await ComposerAsync(Manager);
        Assert.Contains("Guardians", manager.Markup);
        var managerDraft = manager.FindComponent<AnnouncementComposerForm>().Instance.Draft;
        Assert.Contains(AudienceRole.Guardians, managerDraft.Roles);
        await DisposeComponentsAsync();

        var teacher = await ComposerAsync(Teacher);
        var teacherDraft = teacher.FindComponent<AnnouncementComposerForm>().Instance.Draft;
        Assert.DoesNotContain(AudienceRole.Guardians, teacherDraft.Roles);
        Assert.Contains(AudienceRole.Students, teacherDraft.Roles);
    }

    [Fact]
    public async Task Schedule_requires_a_future_time_by_server_clock()
    {
        var page = await ComposerAsync(Manager);
        _api.Now = () => Now.AddDays(5); // the server clock is ahead of this device: tomorrow is already the past for it
        var form = page.FindComponent<AnnouncementComposerForm>().Instance;
        form.Draft.TitleAr = "ع";
        form.Draft.TitleEn = "T";
        form.Draft.BodyEn = "Body";
        form.Draft.Scope = AudienceScopeKind.Organization;
        form.Draft.Mode = PublishMode.Schedule;
        form.Draft.PublishAtLocal = Now.AddDays(1);

        await page.InvokeAsync(() => form.OnPublish.InvokeAsync(form.Draft));

        page.WaitForElement("[data-testid=annc-error]");
        Assert.Empty((await _api.ListAsync(new AnnouncementQuery())).Items);
    }

    [Fact]
    public async Task Expiry_must_follow_publish()
    {
        var page = await ComposerAsync(Manager);
        var form = page.FindComponent<AnnouncementComposerForm>().Instance;
        form.Draft.TitleAr = "ع";
        form.Draft.TitleEn = "T";
        form.Draft.BodyEn = "Body";
        form.Draft.Scope = AudienceScopeKind.Organization;
        form.Draft.Mode = PublishMode.Schedule;
        form.Draft.PublishAtLocal = Now.AddDays(5);
        form.Draft.ExpiresOn = DateOnly.FromDateTime(Now.AddDays(2));

        await page.InvokeAsync(() => form.OnPublish.InvokeAsync(form.Draft));

        page.WaitForElement("[data-testid=annc-error]");
        Assert.Empty((await _api.ListAsync(new AnnouncementQuery())).Items);
    }

    [Fact]
    public async Task A_valid_publish_creates_it_and_returns_to_the_list()
    {
        var page = await ComposerAsync(Manager);
        var form = page.FindComponent<AnnouncementComposerForm>().Instance;
        form.Draft.TitleAr = "ع";
        form.Draft.TitleEn = "Parents evening";
        form.Draft.BodyEn = "Join us";
        form.Draft.Scope = AudienceScopeKind.Organization;

        await page.InvokeAsync(() => form.OnPublish.InvokeAsync(form.Draft));

        page.WaitForAssertion(() => Assert.Contains("announcements", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri));
        var created = Assert.Single((await _api.ListAsync(new AnnouncementQuery())).Items);
        Assert.Equal(AnnouncementStatuses.Published, created.Status);
    }

    private async Task<AnnouncementDto> SeedAsync(bool publish, bool pin = false, string titleEn = "Title", string? body = "Body") =>
        await _api.CreateAsync(new CreateAnnouncementRequest(FakeMessagingApi.UnitA, "ع", titleEn, body, body,
            [new AnnouncementTargetDto(AnnouncementTargetKinds.Organization, null, true)], null, null, null, pin, true, null, publish));

    [Fact]
    public async Task Pinned_announcements_come_first()
    {
        await SeedAsync(true, titleEn: "Plain");
        var pinned = await SeedAsync(true, pin: true, titleEn: "Pinned one");
        await PrepareAsync([Permissions.Announcements.View]);

        var page = Render<OrgAnnouncementsPage>();
        page.WaitForElement("[data-testid=anns-feed]");

        Assert.Equal($"ann-card-{pinned.Id}", page.FindAll("[data-testid^=ann-card-]")[0].GetAttribute("data-testid"));
    }

    [Fact]
    public async Task Body_is_sanitized_on_render()
    {
        await SeedAsync(true, body: "<img src=x onerror=alert(1)><script>alert(2)</script>hello");
        await PrepareAsync([Permissions.Announcements.View]);

        var page = Render<OrgAnnouncementsPage>();
        page.WaitForElement("[data-testid=anns-feed]");

        Assert.Empty(page.FindAll("img[onerror], script")); // shown as text, never as markup
        Assert.Contains("hello", page.Markup);
    }

    [Fact]
    public async Task Student_sees_no_manage_controls_and_reading_marks_it_read()
    {
        var a = await SeedAsync(true);
        await PrepareAsync([Permissions.Announcements.View]);

        var page = Render<OrgAnnouncementsPage>();
        page.WaitForElement("[data-testid=anns-feed]");

        Assert.Empty(page.FindAll("[data-testid=anns-new], [data-testid=anns-tabs]"));
        await page.Find($"[data-testid=ann-read-{a.Id}]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid=ann-unread-{a.Id}]")));
    }

    [Fact]
    public async Task Manage_tab_lists_drafts_and_publishes_or_withdraws_them()
    {
        var draft = await SeedAsync(false);
        await PrepareAsync(Manager);

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("tab", "manage"));
        var page = Render<OrgAnnouncementsPage>();
        page.WaitForElement($"[data-testid=ann-row-{draft.Id}]");
        Assert.Contains("Draft", page.Find($"[data-testid=ann-status-{draft.Id}]").TextContent);

        await page.Find($"[data-testid=ann-publish-{draft.Id}]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Contains("Published", page.Find($"[data-testid=ann-status-{draft.Id}]").TextContent));
        Assert.Empty(page.FindAll($"[data-testid=ann-edit-{draft.Id}]")); // only a draft or scheduled one is editable

        await page.Find($"[data-testid=ann-withdraw-{draft.Id}]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Contains("Withdrawn", page.Find($"[data-testid=ann-status-{draft.Id}]").TextContent));
    }

    [Fact]
    public async Task A_page_without_view_or_manage_is_forbidden_and_the_composer_needs_manage()
    {
        await PrepareAsync([Permissions.Courses.View]);
        var list = Render<OrgAnnouncementsPage>();
        list.WaitForAssertion(() => Assert.Empty(list.FindAll("[data-testid=anns-title]")));
        await DisposeComponentsAsync();

        await PrepareAsync([Permissions.Announcements.View]);
        var composer = Render<AnnouncementComposerPage>();
        composer.WaitForAssertion(() => Assert.Empty(composer.FindAll("[data-testid=annc-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        await PrepareAsync(Manager, "ar");

        var page = Render<OrgAnnouncementsPage>();

        Assert.Equal("الإعلانات", page.WaitForElement("[data-testid=anns-title]").TextContent.Trim());
    }
}
