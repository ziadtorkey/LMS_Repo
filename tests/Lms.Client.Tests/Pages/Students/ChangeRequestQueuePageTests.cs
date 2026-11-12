using System.Globalization;
using Bunit;
using Lms.Client.Pages.Students;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Students;

public sealed class ChangeRequestQueuePageTests : StudentsTestContext
{
    private readonly FakeChangeRequestsApi _api = new();
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public ChangeRequestQueuePageTests() => Services.AddSingleton<IChangeRequestsApi>(_api);

    private async Task<IRenderedComponent<ChangeRequestQueuePage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<ChangeRequestQueuePage>();
    }

    private StudentChangeRequestDto Pending => _api.All.First(r => r.Status == ChangeRequestStatuses.Pending);

    private async Task OpenAsync(IRenderedComponent<ChangeRequestQueuePage> cut, StudentChangeRequestDto request)
    {
        _ = cut.WaitForElement($"[data-testid=queue-open-{request.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=detail-changes]");
    }

    [Fact]
    public async Task Queue_defaults_to_pending_and_filters_by_status()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.ApproveChanges);

        cut.WaitForElement($"[data-testid=queue-open-{Pending.Id}]");
        Assert.Equal(ChangeRequestStatuses.Pending, _api.LastQuery!.Status);
        Assert.DoesNotContain("Omar Khaled", cut.Markup); // the approved one is not in the pending list

        var status = StringSelect(cut, "queue-filter-status");
        await cut.InvokeAsync(() => status.Instance.ValueChanged.InvokeAsync(null));
        await cut.WaitForAssertionAsync(() => Assert.Null(_api.LastQuery!.Status));
        await cut.WaitForAssertionAsync(() => Assert.Contains("Omar Khaled", cut.Markup));
    }

    [Fact]
    public async Task Queue_detail_shows_before_and_after()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.ApproveChanges);
        await OpenAsync(cut, Pending);

        Assert.Equal("Ahmed Saeed", Dialogs.Find("[data-testid=before-NameEn]").TextContent);
        Assert.Equal("Ahmed Said", Dialogs.Find("[data-testid=after-NameEn]").TextContent);
        Assert.Equal("2014-03-05", Dialogs.Find("[data-testid=before-DateOfBirth]").TextContent);
        Assert.Equal("2014-03-06", Dialogs.Find("[data-testid=after-DateOfBirth]").TextContent);
    }

    [Fact]
    public async Task Nationality_before_and_after_are_localized_names()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.ApproveChanges);
        var request = _api.All.First(r => r.Changes.Any(c => c.Field == StudentFields.Nationality));
        await OpenAsync(cut, request);

        Assert.Equal(Nationalities.Find("EG")!.NameEn, Dialogs.Find("[data-testid=before-Nationality]").TextContent);
        Assert.Equal(Nationalities.Find("SA")!.NameEn, Dialogs.Find("[data-testid=after-Nationality]").TextContent);
    }

    [Fact]
    public async Task Approve_sends_the_optional_comment()
    {
        var id = Pending.Id; // after the decision another request becomes the first pending one
        var cut = await RenderPageAsync(Permissions.Students.ApproveChanges);
        await OpenAsync(cut, Pending);
        Dialogs.Find("textarea[data-testid=decision-comment]").Change("Checked the ID card");

        await Dialogs.Find("[data-testid=decision-approve]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_api.Approved));
        Assert.Equal((id, "Checked the ID card"), _api.Approved[0]);
    }

    [Fact]
    public async Task Reject_requires_comment()
    {
        var cut = await RenderPageAsync(Permissions.Students.ApproveChanges);
        await OpenAsync(cut, Pending);

        await Dialogs.Find("[data-testid=decision-reject]").ClickAsync(new());
        Assert.Empty(_api.Rejected);

        Dialogs.Find("textarea[data-testid=decision-comment]").Change("Does not match the record");
        await Dialogs.Find("[data-testid=decision-reject]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_api.Rejected));
        Assert.Equal("Does not match the record", _api.Rejected[0].Comment);
    }

    [Fact]
    public async Task Stale_request_shows_the_localized_error_and_stays_open()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.ApproveChanges);
        await OpenAsync(cut, Pending);
        _api.Failure = new ApiProblemException(409, "change_request_stale", null, null);

        await Dialogs.Find("[data-testid=decision-approve]").ClickAsync(new());

        Assert.Contains("The student's data changed after the request was sent.", Dialogs.WaitForElement("[data-testid=dialog-error]").TextContent);
        Assert.Empty(_api.Approved);
    }

    [Fact]
    public async Task Queue_is_forbidden_without_Students_ApproveChanges()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.View);

        cut.WaitForAssertion(() => Assert.Contains("You do not have permission", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=queue-title]"));
    }

    [Fact]
    public async Task Queue_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Students.ApproveChanges);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<ChangeRequestQueuePage>());

        Assert.Contains("طلبات التعديل", rtl.WaitForElement("[data-testid=queue-title]").TextContent);
        rtl.WaitForElement($"[data-testid=queue-open-{Pending.Id}]");
        Assert.Contains("أحمد سعيد", rtl.Markup);
    }
}
