using System.Globalization;
using Bunit;
using Lms.Client.Components.YearEnd;
using Lms.Client.Pages.AcademicYears;
using Lms.Client.Pages.GradeChanges;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.AcademicYears;
using Lms.Shared.Authorization;
using Lms.Shared.YearEnd;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>M7 H2: close a year (FR-YEAR-04, D79) and approve grade changes after the close (FR-GRD-06, D80), against the year-end fakes.</summary>
public sealed class YearEndH2Tests : ClientTestContext
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Today);
    private readonly FakeYearsApi _years = new();
    private readonly FakeYearCloseApi _close = new();
    private readonly FakeGradeChangesApi _changes = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public YearEndH2Tests()
    {
        Services.AddSingleton<IYearsApi>(_years);
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
        Services.AddSingleton<IYearCloseApi>(_close);
        Services.AddSingleton<IGradeChangesApi>(_changes);
    }

    private new async Task GrantAsync(params string[] permissions)
    {
        await base.GrantAsync(permissions);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private void Providers()
    {
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }

    private AcademicYearDto _year = default!;

    private AcademicYearDto YearNow(string status) => _year =
        _years.AddYear(FakeUnits.SchoolId, "٢٠٢٦/٢٠٢٧", FakeYearCloseApi.DemoNameEn, Today.AddDays(-30), Today.AddDays(30), status);

    private async Task<IRenderedComponent<AcademicYearsPage>> OpenYearsAsync(string status = AcademicYearStatuses.Active, bool canClose = true)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(canClose ? [Permissions.OrgUnits.View, Permissions.AcademicYears.View, Permissions.AcademicYears.Close] : [Permissions.OrgUnits.View, Permissions.AcademicYears.View]);
        Providers();
        var year = YearNow(status);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("unit", FakeUnits.SchoolId.ToString()));
        var page = Render<AcademicYearsPage>();
        page.WaitForElement($"[data-testid=year-{year.Id}]");
        return page;
    }

    private async Task OpenCloseDialogAsync(IRenderedComponent<AcademicYearsPage> page)
    {
        page.Find("[data-testid^=year-close-]").Click(); // sync: a click that opens a dialog must not be awaited
        _dialogs.WaitForElement("[data-testid=year-close-confirm-input]");
    }

    // ----- close year

    [Fact]
    public async Task Close_dialog_lists_what_will_be_locked_and_what_is_worth_checking()
    {
        var page = await OpenYearsAsync();

        await OpenCloseDialogAsync(page);

        Assert.Contains("12", _dialogs.Find("[data-testid=year-close-effects]").TextContent);
        Assert.Contains("480", _dialogs.Find("[data-testid=year-close-effects]").TextContent);
        Assert.Contains("(3)", _dialogs.Find("[data-testid=year-close-warnings]").TextContent); // unpublished grade items warn with their count (D79)
        Assert.Empty(_dialogs.FindAll("[data-testid=year-close-blocking]"));
    }

    [Fact]
    public async Task A_blocking_problem_shows_an_alert_and_keeps_Close_off_even_with_the_name_typed()
    {
        _close.Blocking.Add(new(YearEndIssueCodes.OpenTerms, 2, null));
        var page = await OpenYearsAsync();
        await OpenCloseDialogAsync(page);

        _dialogs.Find("[data-testid=year-close-confirm-input] input, input[data-testid=year-close-confirm-input]").Input(FakeYearCloseApi.DemoNameEn);

        Assert.NotEmpty(_dialogs.FindAll("[data-testid=year-close-blocking]"));
        Assert.True(_dialogs.Find("[data-testid=year-close-confirm]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Close_requires_the_typed_year_name()
    {
        var page = await OpenYearsAsync();
        await OpenCloseDialogAsync(page);
        var input = _dialogs.Find("[data-testid=year-close-confirm-input] input, input[data-testid=year-close-confirm-input]");

        Assert.True(_dialogs.Find("[data-testid=year-close-confirm]").HasAttribute("disabled"));
        input.Input("2025/2026");
        Assert.True(_dialogs.Find("[data-testid=year-close-confirm]").HasAttribute("disabled"));
        input.Input("  " + FakeYearCloseApi.DemoNameEn + " "); // trimmed

        _dialogs.WaitForAssertion(() => Assert.False(_dialogs.Find("[data-testid=year-close-confirm]").HasAttribute("disabled")));
    }

    [Fact]
    public async Task Closing_asks_the_server_and_says_the_year_is_closed()
    {
        var page = await OpenYearsAsync();
        await OpenCloseDialogAsync(page);
        _dialogs.Find("[data-testid=year-close-confirm-input] input, input[data-testid=year-close-confirm-input]").Input(FakeYearCloseApi.DemoNameEn);

        await _dialogs.Find("[data-testid=year-close-confirm]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.True(_close.IsClosed(_year.Id)));
        page.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=year-close-dialog]")));
    }

    [Fact]
    public async Task The_close_action_needs_the_permission()
    {
        var page = await OpenYearsAsync(AcademicYearStatuses.Active, canClose: false);
        Assert.Empty(page.FindAll("[data-testid^=year-close-]"));
    }

    [Fact]
    public async Task Only_an_active_year_can_be_closed()
    {
        var page = await OpenYearsAsync(AcademicYearStatuses.Planned);
        Assert.Empty(page.FindAll("[data-testid^=year-close-]"));
    }

    [Fact]
    public async Task The_closing_report_lists_the_classes()
    {
        var page = await OpenYearsAsync(AcademicYearStatuses.Closed);
        await _close.CloseAsync(_year.Id, new CloseYearRequest(FakeYearCloseApi.DemoNameEn));

        page.Find("[data-testid^=year-closing-report-]").Click();

        _dialogs.WaitForElement("[data-testid=closing-report]");
        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid^=closing-row-]")));
    }

    // ----- grade changes

    private async Task<IRenderedComponent<GradeChangeQueuePage>> OpenQueueAsync(string culture = "en", bool approver = true)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        _changes.CanApprove = true;
        _changes.Me = Guid.NewGuid();
        Api.Me = Api.Me with { UserId = Guid.NewGuid() }; // the signed-in approver is not the requester
        await GrantAsync(approver ? [Permissions.Grades.ChangeApprove] : [Permissions.Grades.ChangeRequest]);
        Providers();
        var requester = _changes.Me;
        await _changes.CreateAsync(Guid.NewGuid(), new CreateGradeChangeRequest(Guid.NewGuid(), Guid.NewGuid(), 80m, "The exam paper was marked with the wrong key.", 1));
        _changes.Me = Guid.NewGuid(); // the queue is read by somebody else
        _ = requester;
        var page = Render<GradeChangeQueuePage>();
        page.WaitForElement(approver ? "[data-testid=gc-queue]" : "[data-testid=gc-forbidden]");
        return page;
    }

    [Fact]
    public async Task The_queue_shows_the_change_the_reason_and_the_waiting_state()
    {
        var page = await OpenQueueAsync();

        Assert.Contains("50 → 80", page.Find("[data-testid^=gc-diff-]").TextContent);
        Assert.Contains("wrong key", page.Find("[data-testid=gc-reason]").TextContent);
        Assert.NotEmpty(page.FindAll("[data-testid=gc-history]"));
        Assert.Equal("true", page.Find("[data-testid=gc-filter-Pending]").GetAttribute("aria-pressed"));
    }

    [Fact]
    public async Task Without_the_approve_permission_the_queue_is_forbidden()
    {
        var page = await OpenQueueAsync(approver: false);
        Assert.NotEmpty(page.FindAll("[data-testid=gc-forbidden]"));
        Assert.Empty(page.FindAll("[data-testid=gc-queue]"));
    }

    [Fact]
    public async Task Approving_moves_the_request_out_of_Waiting()
    {
        var page = await OpenQueueAsync();

        await page.Find("[data-testid^=gc-approve-]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=gc-empty]")));
        var approved = await _changes.ListAsync(new GradeChangeQuery(State: GradeChangeStates.Approved));
        Assert.Single(approved.Items);
    }

    [Fact]
    public async Task Rejecting_needs_a_note()
    {
        var page = await OpenQueueAsync();
        Assert.True(page.Find("[data-testid^=gc-reject-]:not([data-testid=gc-reject-note])").HasAttribute("disabled"));

        page.Find("[data-testid=gc-reject-note] textarea, textarea[data-testid=gc-reject-note]").Input("The paper was already re-marked.");
        page.WaitForAssertion(() => Assert.False(page.Find("[data-testid^=gc-reject-]:not([data-testid=gc-reject-note])").HasAttribute("disabled")));
        await page.Find("[data-testid^=gc-reject-]:not([data-testid=gc-reject-note])").ClickAsync(new());

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=gc-empty]")));
        var rejected = await _changes.ListAsync(new GradeChangeQuery(State: GradeChangeStates.Rejected));
        Assert.Equal("The paper was already re-marked.", rejected.Items.Single().DecisionNote);
    }

    [Fact]
    public async Task The_requester_cannot_approve_or_reject_their_own_request()
    {
        Culture.Current = new CultureInfo("en");
        var me = Guid.NewGuid();
        _changes.CanApprove = true;
        _changes.Me = me;
        Api.Me = Api.Me with { UserId = me };
        await GrantAsync(Permissions.Grades.ChangeApprove);
        Providers();
        await _changes.CreateAsync(Guid.NewGuid(), new CreateGradeChangeRequest(Guid.NewGuid(), Guid.NewGuid(), 70m, "I entered it under the wrong student by mistake.", 1));
        var page = Render<GradeChangeQueuePage>();
        page.WaitForElement("[data-testid=gc-queue]");

        Assert.True(page.Find("[data-testid^=gc-approve-]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-testid^=gc-reject-]:not([data-testid=gc-reject-note])").HasAttribute("disabled"));
        Assert.NotEmpty(page.FindAll("[data-testid=gc-own-reason]")); // the reason is in words
        Assert.Contains("Your request", page.Find("[data-testid^=gc-row-]").TextContent);
    }

    [Fact]
    public async Task The_request_dialog_needs_a_reason_of_at_least_20_characters()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Grades.ChangeRequest);
        Providers();
        var dialogs = Services.GetRequiredService<IDialogService>();
        _ = dialogs.ShowAsync<GradeChangeRequestDialog>("t", new DialogParameters<GradeChangeRequestDialog>
        {
            { x => x.ClassCourseId, Guid.NewGuid() }, { x => x.GradeItemId, Guid.NewGuid() }, { x => x.StudentId, Guid.NewGuid() },
            { x => x.StudentName, "Sara" }, { x => x.ItemName, "Quiz 1" }, { x => x.CurrentValue, 12m }, { x => x.Version, 1 },
        });
        _dialogs.WaitForElement("[data-testid=grade-request]");
        Assert.True(_dialogs.Find("[data-testid=grade-request-submit]").HasAttribute("disabled"));

        _dialogs.Find("textarea").Input("too short");
        Assert.True(_dialogs.Find("[data-testid=grade-request-submit]").HasAttribute("disabled"));
        _dialogs.Find("textarea").Input("The first quiz was marked against the wrong answer key.");

        _dialogs.WaitForAssertion(() => Assert.False(_dialogs.Find("[data-testid=grade-request-submit]").HasAttribute("disabled")));
        Assert.Equal("12", _dialogs.Find("[data-testid=grade-request-current]").TextContent.Trim()); // read only
    }

    [Fact]
    public async Task A_second_request_for_the_same_grade_says_one_is_already_waiting()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Grades.ChangeRequest);
        Providers();
        var item = Guid.NewGuid();
        var student = Guid.NewGuid();
        await _changes.CreateAsync(Guid.NewGuid(), new CreateGradeChangeRequest(item, student, 60m, "A first request for this grade, with a reason.", 1));
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<GradeChangeRequestDialog>("t", new DialogParameters<GradeChangeRequestDialog>
        {
            { x => x.ClassCourseId, Guid.NewGuid() }, { x => x.GradeItemId, item }, { x => x.StudentId, student },
            { x => x.StudentName, "Sara" }, { x => x.ItemName, "Quiz 1" }, { x => x.CurrentValue, 12m }, { x => x.Version, 1 },
        });
        _dialogs.WaitForElement("[data-testid=grade-request]");
        _dialogs.Find("textarea").Input("A second request for the same grade, with a reason.");

        await _dialogs.Find("[data-testid=grade-request-submit]").ClickAsync(new());

        _dialogs.WaitForAssertion(() => Assert.NotEmpty(_dialogs.FindAll("[data-testid=grade-request-error]")));
    }

    [Fact]
    public void The_locked_notice_is_a_status_with_the_rule_in_words()
    {
        Culture.Current = new CultureInfo("en");
        var cut = Render<LockedNotice>();

        Assert.Equal("status", cut.Find("[data-testid=locked-notice]").GetAttribute("role"));
        Assert.Contains("manager's approval", cut.Markup);
    }

    [Fact]
    public async Task Queue_page_renders_rtl_in_arabic()
    {
        var page = await OpenQueueAsync("ar");

        Assert.True(Culture.IsRightToLeft);
        Assert.Contains("تعديلات الدرجات", page.Find("[data-testid=gc-title]").TextContent);
        Assert.Equal("ltr", page.Find("[data-testid^=gc-diff-]").GetAttribute("dir")); // numbers keep their order inside Arabic text
    }

    [Fact]
    public void The_queue_is_one_column_below_900px_and_two_above()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var css = File.ReadAllText(Path.Combine(dir!, "src", "Lms.Client", "wwwroot", "css", "app.css"));

        Assert.Contains(".lms-gc { display: grid; grid-template-columns: minmax(0, 1fr);", css);
        Assert.Contains("@media (min-width: 900px) { .lms-gc {", css);
    }
}
