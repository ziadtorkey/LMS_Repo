using System.Globalization;
using Bunit;
using Lms.Client.Navigation;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class ExceptionQueuePageTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeEnrollmentApi _enrollment = new();

    public ExceptionQueuePageTests() => Services.AddSingleton<IEnrollmentApi>(_enrollment);

    private EnrollmentExceptionDto Pending => _enrollment.Exceptions.First(e => e.Status == ExceptionStatuses.Pending);

    private async Task<IRenderedComponent<ExceptionQueuePage>> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions.Length == 0 ? [Permissions.Enrollment.ApproveException] : permissions);
        RenderProviders();
        Render<MudSnackbarProvider>();
        var page = Render<ExceptionQueuePage>();
        page.WaitForAssertion(() => Assert.Contains("Omar Khaled", page.Markup), Wait);
        return page;
    }

    private async Task OpenAsync(IRenderedComponent<ExceptionQueuePage> page, EnrollmentExceptionDto request)
    {
        _ = page.WaitForElement($"[data-testid=exception-open-{request.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=decide-failed]");
    }

    [Fact]
    public async Task Queue_defaults_to_pending_shows_failed_restriction_chips_and_filters_status()
    {
        var page = await RenderPageAsync();

        Assert.Equal(ExceptionStatuses.Pending, _enrollment.LastExceptionQuery!.Status);
        Assert.DoesNotContain("Laila Hassan", page.Markup); // the rejected one
        Assert.NotEmpty(page.FindAll($"[data-testid=failed-{Pending.Id}-Gender]"));
        var sara = _enrollment.Exceptions.First(e => e.StudentCode == "S-1004");
        Assert.NotEmpty(page.FindAll($"[data-testid=failed-{sara.Id}-Religion]"));
        Assert.NotEmpty(page.FindAll($"[data-testid=failed-{sara.Id}-Gender]"));

        var status = StringSelect(page, "exceptions-filter-status");
        await page.InvokeAsync(() => status.Instance.ValueChanged.InvokeAsync(null));
        await page.WaitForAssertionAsync(() => Assert.Contains("Laila Hassan", page.Markup), Wait);
        Assert.Null(_enrollment.LastExceptionQuery!.Status);
    }

    [Fact]
    public async Task Detail_shows_what_failed_and_the_requesters_reason()
    {
        var page = await RenderPageAsync();
        await OpenAsync(page, Pending);

        Assert.Contains("Omar Khaled", Dialogs.Find("[data-testid=decide-student]").TextContent);
        Assert.Contains("Gender", Dialogs.Find("[data-testid=decide-failed]").TextContent);
        Assert.Contains("The student studied the course abroad.", Dialogs.Find("[data-testid=decide-reason]").TextContent);
    }

    [Fact]
    public async Task Approve_sends_request_and_removes_row()
    {
        var page = await RenderPageAsync();
        var id = Pending.Id;
        await OpenAsync(page, Pending);
        Dialogs.Find("textarea[data-testid=decision-comment]").Change("Approved by the counselor");

        await Dialogs.Find("[data-testid=decision-approve]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Single(_enrollment.ExceptionApprovals), Wait);
        Assert.Equal((id, "Approved by the counselor"), _enrollment.ExceptionApprovals[0]);
        await page.WaitForAssertionAsync(() => Assert.Empty(page.FindAll($"[data-testid=exception-open-{id}]")), Wait); // no longer pending
    }

    [Fact]
    public async Task Reject_requires_comment()
    {
        var page = await RenderPageAsync();
        await OpenAsync(page, Pending);

        await Dialogs.Find("[data-testid=decision-reject]").ClickAsync(new());
        Assert.Empty(_enrollment.ExceptionRejections);

        Dialogs.Find("textarea[data-testid=decision-comment]").Change("Not justified");
        await Dialogs.Find("[data-testid=decision-reject]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Single(_enrollment.ExceptionRejections), Wait);
        Assert.Equal("Not justified", _enrollment.ExceptionRejections[0].Comment);
    }

    [Fact]
    public async Task Closed_request_shows_the_localized_error_and_stays_open()
    {
        var page = await RenderPageAsync();
        await OpenAsync(page, Pending);
        _enrollment.Failure = new ApiProblemException(409, "restriction_not_met", null, null);

        await Dialogs.Find("[data-testid=decision-approve]").ClickAsync(new());

        Assert.Contains("The student does not meet the course restrictions.", (await Dialogs.WaitForElementAsync("[data-testid=dialog-error]", Wait)).TextContent);
        Assert.Empty(_enrollment.ExceptionApprovals);
    }

    [Fact]
    public async Task Requester_sees_own_requests_with_cancel_and_no_decision_buttons()
    {
        var page = await RenderPageAsync(Permissions.Enrollment.RequestException);
        var id = Pending.Id;
        Assert.Empty(page.FindAll("[data-testid=exceptions-filter-unit]"));

        await page.Find($"[data-testid=exception-cancel-{id}]").ClickAsync(new());

        await page.WaitForAssertionAsync(() => Assert.Equal([id], _enrollment.ExceptionCancellations), Wait);
        await OpenAsync2(page);
    }

    private async Task OpenAsync2(IRenderedComponent<ExceptionQueuePage> page)
    {
        var other = _enrollment.Exceptions.First(e => e.Status == ExceptionStatuses.Pending);
        _ = page.WaitForElement($"[data-testid=exception-open-{other.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=decide-failed]");
        Assert.Empty(Dialogs.FindAll("[data-testid=decision-approve]"));
        Assert.Empty(Dialogs.FindAll("[data-testid=decision-reject]"));
    }

    [Fact]
    public async Task Approvers_do_not_see_cancel_only_the_requester_does()
    {
        var approver = await RenderPageAsync(Permissions.Enrollment.ApproveException);
        Assert.NotEmpty(approver.FindAll($"[data-testid=exception-open-{Pending.Id}]"));
        Assert.Empty(approver.FindAll("[data-testid^=exception-cancel-]")); // approvers decide, they do not withdraw someone else's request

        // A user who both approves and requests is still an approver here: the queue is the approvers' view.
        await GrantAsync(Permissions.Enrollment.ApproveException, Permissions.Enrollment.RequestException);
        var both = Render<ExceptionQueuePage>();
        both.WaitForElement($"[data-testid=exception-open-{Pending.Id}]");
        Assert.Empty(both.FindAll("[data-testid^=exception-cancel-]"));
    }

    [Fact]
    public async Task Page_is_forbidden_without_either_permission()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Students.View);
        RenderProviders();
        var page = Render<ExceptionQueuePage>();

        page.WaitForAssertion(() => Assert.Contains("You do not have permission", page.Markup));
        Assert.Empty(page.FindAll("[data-testid=exceptions-title]"));
    }

    [Fact]
    public void Nav_hidden_without_permission()
    {
        var item = NavCatalog.Items.Single(i => i.ResourceKey == "Nav_Exceptions");

        Assert.Equal("enrollment-exceptions", item.Href);
        Assert.Equal(Permissions.Enrollment.ApproveException, item.RequiredPermission);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Enrollment.ApproveException);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<ExceptionQueuePage>());

        Assert.Contains("استثناءات التسجيل", rtl.WaitForElement("[data-testid=exceptions-title]").TextContent);
        var table = rtl.WaitForElement(".mud-table-body");
        rtl.WaitForAssertion(() => Assert.Contains("عمر خالد", rtl.Find(".mud-table-body").TextContent), Wait);
        Assert.Contains("الجنس", table.TextContent);
        Assert.Contains("قيد الانتظار", rtl.Find(".mud-table-body").TextContent);
    }
}
