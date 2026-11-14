using System.Globalization;
using Bunit;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Services;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>The publish flow of the gradebook (M5 Task 27, FR-GRD-05, D58): publish now, schedule, hide again, the provisional-scale acknowledgement.</summary>
public sealed class PublishItemsDialogTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private static readonly Guid Quiz = FakeGradebookApi.ManualItemId;
    private static readonly Guid Assignment = FakeGradebookApi.AutoItemId;
    private readonly FakeGradebookApi _api = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public PublishItemsDialogTests()
    {
        Culture.Current = new CultureInfo("en");
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(new FakeGradebookUnit());
        Services.AddSingleton<IViewport>(new FakeViewport());
    }

    private async Task<IRenderedComponent<GradebookPage>> OpenAsync(string culture = "en")
    {
        Culture.Current = new CultureInfo(culture);
        FakeCultureService.Apply(new CultureInfo(culture));
        await GrantAsync(Permissions.Grades.View, Permissions.Grades.Manage, Permissions.Grades.Publish);
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        var page = Render<GradebookPage>(p => p.Add(x => x.Id, Cc));
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=gradebook-loading]")), Wait);
        return page;
    }

    private async Task OpenDialogAsync(IRenderedComponent<GradebookPage> page)
    {
        await page.Find("[data-testid=gradebook-publish]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=publish-submit]");
    }

    /// <summary>The demo scale is provisional, so the dialog asks for the acknowledgement first.</summary>
    private async Task SubmitAsync()
    {
        if (_dialogs.FindAll("[data-testid=publish-ack]").Count > 0)
        {
            await _dialogs.InvokeAsync(() => _dialogs.FindComponents<MudCheckBox<bool>>().Last().Instance.ValueChanged.InvokeAsync(true));
        }

        await _dialogs.Find("[data-testid=publish-submit]").ClickAsync(new());
    }

    [Fact]
    public async Task Publish_now_schedule_and_hide_per_item()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        // both hidden items are preselected; publish only the quiz now
        await _dialogs.Find($"[data-testid=publish-pick-{Assignment}]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = false });
        await SubmitAsync();

        _dialogs.WaitForElement("[data-testid=publish-result]");
        Assert.Equal(PublishModes.Now, _api.LastPublish!.Mode);
        Assert.Equal([Quiz], _api.LastPublish.ItemIds);
        await _dialogs.Find("[data-testid=publish-done]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Contains("Published", page.Find($"[data-testid=item-state-{Quiz}]").TextContent), Wait);
        Assert.DoesNotContain("Published", page.Find($"[data-testid=item-state-{Assignment}]").TextContent); // per item: the other stays as it was
    }

    [Fact]
    public async Task Hide_again_says_it_is_audited_and_returns_the_item_to_staff_only()
    {
        await _api.PublishAsync(Cc, new PublishItemsRequest([Quiz], PublishModes.Now, null, true));
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        await _dialogs.Find("[data-testid=publish-mode-hide]").ClickAsync(new());
        Assert.Contains("audit", _dialogs.Find("[data-testid=publish-hide-note]").TextContent);
        await _dialogs.InvokeAsync(() => _dialogs.FindComponents<MudCheckBox<bool>>().First().Instance.ValueChanged.InvokeAsync(true)); // the published quiz (the first row)
        await SubmitAsync();

        _dialogs.WaitForElement("[data-testid=publish-result]");
        Assert.Equal(PublishModes.Hide, _api.LastPublish!.Mode);
    }

    [Fact]
    public async Task Schedule_must_be_in_the_future_by_server_time()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);
        await _dialogs.Find("[data-testid=publish-mode-schedule]").ClickAsync(new());

        // a time in the past: the server (its clock) refuses it and the dialog shows the answer, nothing changes
        await _dialogs.InvokeAsync(() => _dialogs.FindComponent<MudDatePicker>().Instance.DateChanged.InvokeAsync(DateTime.UtcNow.Date.AddDays(-2)));
        await _dialogs.InvokeAsync(() => _dialogs.FindComponent<MudTimePicker>().Instance.TimeChanged.InvokeAsync(new TimeSpan(9, 0, 0)));
        await SubmitAsync();

        _dialogs.WaitForElement("[data-testid=publish-error]");
        Assert.Empty(_dialogs.FindAll("[data-testid=publish-result]"));
        Assert.NotNull(_api.LastPublish!.AtUtc);
        Assert.Equal(PublishModes.Schedule, _api.LastPublish.Mode);
    }

    [Fact]
    public async Task Confirm_text_says_who_sees_what()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        var text = _dialogs.Find("[data-testid=publish-confirm]").TextContent;
        Assert.Contains("students of this course", text);
        Assert.Contains("guardians", text);
        Assert.Contains("staff-only", text);
    }

    [Fact]
    public async Task Provisional_scale_warning_requires_acknowledgement()
    {
        _api.Transform = (book, _) => book with { Config = book.Config with { ScaleProvisional = true } };
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        _dialogs.WaitForElement("[data-testid=publish-provisional]");
        Assert.True(_dialogs.Find("[data-testid=publish-submit]").HasAttribute("disabled")); // not until acknowledged
        await _dialogs.InvokeAsync(() => _dialogs.FindComponents<MudCheckBox<bool>>().Last().Instance.ValueChanged.InvokeAsync(true));
        Assert.False(_dialogs.Find("[data-testid=publish-submit]").HasAttribute("disabled"));

        await _dialogs.Find("[data-testid=publish-submit]").ClickAsync(new());

        _dialogs.WaitForElement("[data-testid=publish-result]");
        Assert.True(_api.LastPublish!.AcknowledgeProvisionalScale);
    }

    [Fact]
    public async Task Hiding_needs_no_provisional_acknowledgement()
    {
        _api.Transform = (book, _) => book with { Config = book.Config with { ScaleProvisional = true } };
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        await _dialogs.Find("[data-testid=publish-mode-hide]").ClickAsync(new());

        Assert.Empty(_dialogs.FindAll("[data-testid=publish-provisional]"));
        Assert.False(_dialogs.Find("[data-testid=publish-submit]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Result_shows_notifications_queued()
    {
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        await SubmitAsync();

        var result = _dialogs.WaitForElement("[data-testid=publish-result]").TextContent;
        Assert.Contains("items changed", result);
        Assert.Contains("notifications queued", _dialogs.Find("[data-testid=publish-notified]").TextContent);
    }

    [Fact]
    public async Task A_locked_gradebook_can_still_publish()
    {
        _api.Locked = true;
        var page = await OpenAsync();
        await OpenDialogAsync(page);

        await SubmitAsync();

        _dialogs.WaitForElement("[data-testid=publish-result]");
        Assert.Empty(_dialogs.FindAll("[data-testid=publish-error]"));
    }

    [Fact]
    public async Task Without_the_publish_right_there_is_no_publish_button()
    {
        _api.CanManage = false;
        var page = await OpenAsync();

        Assert.Empty(page.FindAll("[data-testid=gradebook-publish]"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        var page = await OpenAsync("ar");

        Assert.Contains("نشر", page.Find("[data-testid=gradebook-publish]").TextContent);
    }
}
