using System.Globalization;
using Bunit;
using Lms.Client.Pages.Students;
using Lms.Client.Services.YearEnd;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Lms.Shared.YearEnd;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.YearEnd;

/// <summary>M7 H5: withdrawal, graduation, re-admission and the archive (FR-STU-08, D25, D81, D82), against the lifecycle fake.</summary>
public sealed class YearEndH5Tests : StudentsTestContext
{
    private static readonly Guid Sara = FakeLifecycleApi.ActiveStudentId;
    private readonly FakeLifecycleApi _lifecycle = new();

    public YearEndH5Tests() => Services.AddSingleton<ILifecycleApi>(_lifecycle);

    private async Task PrepareAsync(string culture, params string[] permissions)
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(permissions);
        await Services.GetRequiredService<Lms.Client.Services.PermissionState>().RefreshAsync();
        RenderProviders();
    }

    private void OpenWithdraw() => OpenDialog<WithdrawDialog>("withdraw-dialog");
    private void OpenGraduate() => OpenDialog<GraduateDialog>("graduate-dialog");

    private void OpenDialog<TDialog>(string testId) where TDialog : Microsoft.AspNetCore.Components.IComponent
    {
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<TDialog>("t", new DialogParameters { { "StudentId", Sara }, { "Name", "Sara" } });
        Dialogs.WaitForElement($"[data-testid={testId}]");
    }

    private async Task PickReasonAsync(string key) =>
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync(key));

    // ----- withdraw

    [Fact]
    public async Task Withdraw_requires_a_reason_and_an_effective_date()
    {
        await PrepareAsync("en", Permissions.Students.Withdraw);
        OpenWithdraw();
        Assert.True(Dialogs.Find("[data-testid=withdraw-confirm]").HasAttribute("disabled")); // no reason yet

        await PickReasonAsync("MovedAway");

        Dialogs.WaitForAssertion(() => Assert.False(Dialogs.Find("[data-testid=withdraw-confirm]").HasAttribute("disabled"))); // the date defaults to today
        Assert.Contains("signs them out", Dialogs.Find("[data-testid=withdraw-effect]").TextContent); // the effect is said first
    }

    [Fact]
    public async Task The_reason_Other_needs_a_note()
    {
        await PrepareAsync("en", Permissions.Students.Withdraw);
        OpenWithdraw();

        await PickReasonAsync(WithdrawalReasons.Other);
        Assert.True(Dialogs.Find("[data-testid=withdraw-confirm]").HasAttribute("disabled"));
        Dialogs.Find("textarea").Input("Left the country");

        Dialogs.WaitForAssertion(() => Assert.False(Dialogs.Find("[data-testid=withdraw-confirm]").HasAttribute("disabled")));
    }

    [Fact]
    public async Task A_future_effective_date_explains_when_sign_in_stops()
    {
        await PrepareAsync("en", Permissions.Students.Withdraw);
        OpenWithdraw();
        Assert.Empty(Dialogs.FindAll("[data-testid=withdraw-future]"));

        var picker = Dialogs.FindComponent<MudDatePicker>();
        var future = DateTime.Today.AddDays(10);
        await Dialogs.InvokeAsync(() => picker.Instance.DateChanged.InvokeAsync(future));

        Dialogs.WaitForAssertion(() => Assert.Contains(future.ToString("yyyy-MM-dd"), Dialogs.Find("[data-testid=withdraw-future]").TextContent));
    }

    [Fact]
    public async Task Withdrawing_sends_the_reason_and_the_student_leaves_the_active_set()
    {
        await PrepareAsync("en", Permissions.Students.Withdraw);
        OpenWithdraw();
        await PickReasonAsync("MovedAway");

        await Dialogs.Find("[data-testid=withdraw-confirm]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.False(_lifecycle.IsActive(Sara)));
        Assert.False(_lifecycle.CanSignIn(Sara)); // FR-SEC-07: sign-in is blocked
        var archive = await _lifecycle.GetArchiveAsync(new ArchiveQuery());
        Assert.Equal(StudentExitKinds.Withdrawn, archive.Items.Single().ExitKind);
        Assert.Equal("MovedAway", archive.Items.Single().ReasonKey);
    }

    [Fact]
    public async Task Withdrawing_a_student_who_is_already_inactive_says_so()
    {
        await _lifecycle.WithdrawAsync(Sara, new WithdrawStudentRequest("MovedAway", null, DateOnly.FromDateTime(DateTime.Today)));
        await PrepareAsync("en", Permissions.Students.Withdraw);
        OpenWithdraw();
        await PickReasonAsync("MovedAway");

        await Dialogs.Find("[data-testid=withdraw-confirm]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Contains("already inactive", Dialogs.Find("[data-testid=withdraw-error]").TextContent));
    }

    [Fact]
    public async Task Graduating_leaves_the_active_set_with_the_exit_kind_Graduated()
    {
        await PrepareAsync("en", Permissions.Students.Withdraw);
        OpenGraduate();

        await Dialogs.Find("[data-testid=graduate-confirm]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.False(_lifecycle.IsActive(Sara)));
        Assert.Equal(StudentExitKinds.Graduated, (await _lifecycle.GetArchiveAsync(new ArchiveQuery())).Items.Single().ExitKind);
    }

    // ----- profile menu

    private async Task<IRenderedComponent<StudentProfilePage>> RenderProfileAsync(params string[] permissions)
    {
        await PrepareAsync("en", permissions);
        return Render<StudentProfilePage>(p => p.Add(x => x.Id, Students.All[0].Summary.Id));
    }

    [Fact]
    public async Task The_student_record_menu_offers_Withdraw_and_Graduate_to_those_who_may()
    {
        var cut = await RenderProfileAsync(Permissions.Students.View, Permissions.Students.Withdraw);
        cut.WaitForElement("[data-testid=profile-name]");

        Assert.NotEmpty(cut.FindAll("[data-testid=student-record-menu]"));
    }

    [Fact]
    public async Task Without_the_permission_there_is_no_student_record_menu()
    {
        var cut = await RenderProfileAsync(Permissions.Students.View, Permissions.Students.Update);
        cut.WaitForElement("[data-testid=profile-name]");

        Assert.Empty(cut.FindAll("[data-testid=student-record-menu]"));
    }

    // ----- archive

    private async Task<IRenderedComponent<StudentArchivePage>> OpenArchiveAsync(string culture, params string[] permissions)
    {
        await _lifecycle.WithdrawAsync(Sara, new WithdrawStudentRequest("MovedAway", null, DateOnly.FromDateTime(DateTime.Today)));
        await PrepareAsync(culture, permissions);
        var page = Render<StudentArchivePage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll($"[data-testid=archive-row-{Sara}]")));
        return page;
    }

    [Fact]
    public async Task A_withdrawn_student_appears_in_the_archive_with_the_reason_in_the_clients_words()
    {
        var page = await OpenArchiveAsync("en", Permissions.Students.ViewArchive);

        Assert.Contains("Moved away", page.Markup);
        Assert.Contains("Withdrawn", page.Markup);
        Assert.Contains("S00001", page.Markup);
    }

    [Fact]
    public async Task The_archive_is_read_only_without_the_readmit_permission()
    {
        var page = await OpenArchiveAsync("en", Permissions.Students.ViewArchive);

        Assert.Empty(page.FindAll("[data-testid^=archive-readmit-]"));
        Assert.NotEmpty(page.FindAll("[data-testid=archive-readonly]"));
    }

    [Fact]
    public async Task Without_the_view_permission_the_archive_is_forbidden()
    {
        await PrepareAsync("en", Permissions.Students.View);
        var page = Render<StudentArchivePage>();
        page.WaitForElement("[data-testid=archive-forbidden]");
    }

    [Fact]
    public async Task Readmitting_keeps_the_code_restores_sign_in_and_empties_the_archive()
    {
        var page = await OpenArchiveAsync("en", Permissions.Students.ViewArchive, Permissions.Students.Readmit);

        page.Find($"[data-testid=archive-readmit-{Sara}]").Click(); // opens a dialog: not awaited
        Dialogs.WaitForElement("[data-testid=readmit-dialog]");
        Assert.True(Dialogs.Find("[data-testid=readmit-confirm]").HasAttribute("disabled")); // a class is needed first
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<MudSelect<Guid?>>().Instance.ValueChanged.InvokeAsync(ClassesApi.All.First().Id));
        Dialogs.WaitForAssertion(() => Assert.False(Dialogs.Find("[data-testid=readmit-confirm]").HasAttribute("disabled")));
        await Dialogs.Find("[data-testid=readmit-confirm]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.True(_lifecycle.IsActive(Sara)));
        Assert.True(_lifecycle.CanSignIn(Sara));
        Assert.Equal("S00001", _lifecycle.CodeOf(Sara)); // D25: the code is kept
        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid=archive-row-{Sara}]")));
    }

    [Fact]
    public async Task The_archive_filters_by_how_the_student_left()
    {
        var page = await OpenArchiveAsync("en", Permissions.Students.ViewArchive);

        await page.InvokeAsync(() => page.FindComponents<MudSelect<string>>().First().Instance.ValueChanged.InvokeAsync(StudentExitKinds.Graduated));

        page.WaitForAssertion(() => Assert.Empty(page.FindAll($"[data-testid=archive-row-{Sara}]")));
    }

    [Fact]
    public async Task Archive_renders_rtl_in_arabic_and_each_cell_is_labelled_for_the_stacked_view()
    {
        var page = await OpenArchiveAsync("ar", Permissions.Students.ViewArchive);

        Assert.True(Culture.IsRightToLeft);
        Assert.Contains("أرشيف الطلاب", page.Find("[data-testid=archive-title]").TextContent);
        Assert.Equal("ltr", page.Find("td [dir=ltr]").GetAttribute("dir")); // codes and dates keep their order
        foreach (var td in page.FindAll("tbody td").Where(td => !string.IsNullOrWhiteSpace(td.TextContent)))
        {
            Assert.False(string.IsNullOrWhiteSpace(td.GetAttribute("data-label")), "a cell has no data-label: " + td.OuterHtml[..Math.Min(100, td.OuterHtml.Length)]);
        }
    }
}
