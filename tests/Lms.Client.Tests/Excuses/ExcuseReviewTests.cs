using System.Globalization;
using Bunit;
using Lms.Client.Pages.Excuses;
using Lms.Client.Services.Excuses;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Excuses;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Excuses;

public sealed class ExcuseReviewTests : StaffTestContext
{
    private readonly FakeExcusesApi _excuses = new FakeExcusesApi().Seed(); // three submitted excuses; signed in as the counselor

    public ExcuseReviewTests() => Services.AddSingleton<IExcusesApi>(_excuses);

    private async Task<IRenderedComponent<ExcuseReviewPage>> RenderAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        await GrantAsync(Permissions.Excuses.Review, Permissions.Excuses.Escalate);
        RenderProviders();
        var page = Render<ExcuseReviewPage>();
        page.WaitForElement("[data-testid^=review-open-]");
        return page;
    }

    private Guid Id(int index) => _excuses.All[index].Id;

    private static Task Select(IRenderedComponent<ExcuseReviewPage> page, Guid id, bool on = true)
    {
        var box = page.FindComponents<MudCheckBox<bool>>().First(c => c.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == $"review-select-{id}");
        return page.InvokeAsync(() => box.Instance.ValueChanged.InvokeAsync(on));
    }

    [Fact]
    public async Task Queue_filters_default_to_submitted()
    {
        _excuses.CurrentUser = FakeExcusesApi.Counselor;
        await _excuses.ApproveAsync(Id(2), new ApproveExcuseRequest(null)); // decided: no longer waiting
        var page = await RenderAsync();

        Assert.Equal(2, page.FindAll("[data-testid^=review-open-]").Count);
        Assert.All(page.FindAll("[data-testid^=review-status-]"), s => Assert.Contains("Submitted", s.TextContent));
    }

    [Fact]
    public async Task Reject_requires_a_comment_before_the_call()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(0)}]").Click();
        page.WaitForElement("[data-testid=review-reject]");

        page.Find("[data-testid=review-reject]").Click();

        Assert.Equal(ExcuseStatuses.Submitted, _excuses.All[0].Status); // nothing was sent
        Assert.Contains("comment", page.Markup, StringComparison.OrdinalIgnoreCase);

        page.Find("textarea[data-testid=review-comment]").Change("The note does not cover these dates");
        page.Find("[data-testid=review-reject]").Click();
        page.WaitForAssertion(() => Assert.Equal(ExcuseStatuses.Rejected, _excuses.All[0].Status));
        Assert.Equal("The note does not cover these dates", _excuses.All[0].DecisionComment);
    }

    [Fact]
    public async Task Approval_says_n_records_become_excused()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(0)}]").Click();

        Assert.Contains("1 record(s) as Excused", page.WaitForElement("[data-testid=review-approve-effect]").TextContent);

        page.Find("[data-testid=review-approve]").Click();
        page.WaitForAssertion(() => Assert.Equal(ExcuseStatuses.Approved, _excuses.All[0].Status));
    }

    [Fact]
    public async Task Escalate_lists_server_targets()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(0)}]").Click();
        page.WaitForElement("[data-testid=review-escalate]").Click();

        Dialogs.WaitForElement("[data-testid=escalate-send]");
        Dialogs.WaitForAssertion(() => Assert.Contains("Manager", Dialogs.Markup)); // the target the server listed, the default one chosen

        Dialogs.Find("[data-testid=escalate-send]").Click();

        page.WaitForAssertion(() => Assert.Equal(ExcuseStatuses.Escalated, _excuses.All[0].Status));
        Assert.NotNull(_excuses.All[0].EscalatedToUserId);
    }

    [Fact]
    public async Task Reviewer_is_submitter_message()
    {
        _excuses.CurrentUser = FakeExcusesApi.Guardian; // the submitter opens the queue
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(0)}]").Click();
        page.WaitForElement("[data-testid=review-approve]").Click();

        var error = page.WaitForElement("[data-testid=review-error]");

        Assert.Equal(L("Error_excuse_reviewer_is_submitter"), error.TextContent.Trim());
        Assert.Equal(ExcuseStatuses.Submitted, _excuses.All[0].Status);
    }

    [Fact]
    public async Task Bulk_approve_confirm_lists_the_count()
    {
        var page = await RenderAsync();
        Assert.Empty(page.FindAll("[data-testid=bulk-approve]")); // nothing selected, no bulk action
        await Select(page, Id(0));
        await Select(page, Id(1));

        page.Find("[data-testid=bulk-approve]").Click();

        Assert.Contains("Approve 2 excuse(s)", Dialogs.WaitForElement("[data-testid=bulk-message]").TextContent);
        Dialogs.Find("[data-testid=bulk-confirm]").Click();
        page.WaitForAssertion(() => Assert.All(_excuses.All.Take(2), e => Assert.Equal(ExcuseStatuses.Approved, e.Status)));
        Assert.Equal(ExcuseStatuses.Submitted, _excuses.All[2].Status);
    }

    [Fact]
    public async Task No_bulk_reject()
    {
        var page = await RenderAsync();
        await Select(page, Id(0));
        await Select(page, Id(1));

        Assert.Empty(page.FindAll("[data-testid*=bulk-reject]")); // a rejection needs its own comment
        Assert.Contains("open the excuse and give a comment", await OpenBulkAsync(page));
    }

    [Fact]
    public async Task Image_preview_uses_the_signed_url()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(0)}]").Click(); // the first seeded excuse carries a PNG

        var image = page.WaitForElement("[data-testid=attachment-image]");

        Assert.StartsWith("data:image/png", image.GetAttribute("src"));
        Assert.Equal("no-referrer", image.GetAttribute("referrerpolicy"));
    }

    [Fact]
    public async Task Pdf_attachment_is_download_only()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(1)}]").Click(); // the second carries a PDF

        var link = page.WaitForElement("[data-testid=attachment-download]");

        Assert.Equal("letter.pdf", link.GetAttribute("download"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
        Assert.Empty(page.FindAll("[data-testid=attachment-image]")); // never rendered inline
        Assert.Empty(page.FindAll("iframe, embed, object"));
    }

    [Fact]
    public async Task An_excuse_without_an_attachment_says_so()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(2)}]").Click();

        Assert.NotNull(page.WaitForElement("[data-testid=attachment-none]"));
    }

    [Fact]
    public async Task Recent_absences_give_context()
    {
        var page = await RenderAsync();
        page.Find($"[data-testid=review-open-{Id(0)}]").Click();

        Assert.Contains("Science", page.WaitForElement("[data-testid=review-recent]").TextContent);
    }

    [Fact]
    public async Task Without_Excuses_Review_the_page_is_forbidden()
    {
        await GrantAsync(Permissions.Attendance.View);
        RenderProviders();

        var page = Render<ExcuseReviewPage>();

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=review-page-title]")));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await RenderAsync("ar");

        Assert.Equal("مراجعة الأعذار", page.Find("[data-testid=review-page-title]").TextContent.Trim());
        Assert.All(page.FindAll(".lms-date-cell"), d => Assert.Equal("ltr", d.GetAttribute("dir")));
    }

    private async Task<string> OpenBulkAsync(IRenderedComponent<ExcuseReviewPage> page)
    {
        page.Find("[data-testid=bulk-approve]").Click();
        return Dialogs.WaitForElement("[data-testid=bulk-message]").TextContent;
    }

    private string L(string key) => Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<Lms.Client.Resources.SharedResource>>()[key].Value;
}
