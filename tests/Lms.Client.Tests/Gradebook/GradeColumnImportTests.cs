using System.Globalization;
using Bunit;
using Lms.Client.Pages.Gradebook;
using Lms.Client.Components.Gradebook;
using Lms.Client.Services;
using Lms.Client.Services.Grading;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Grading;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Gradebook;

/// <summary>Grade column import and export (M5 Task 28, FR-GRD-04, L9): xlsx only, preview, apply with the token, expired token, download.</summary>
public sealed class GradeColumnImportTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid Cc = FakeGradebookApi.DemoClassCourseId;
    private readonly FakeGradebookApi _api = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public GradeColumnImportTests()
    {
        Services.AddSingleton<IGradebookApi>(_api);
        Services.AddSingleton<IGradebookUnit>(new FakeGradebookUnit());
        Services.AddSingleton<IViewport>(new FakeViewport());
    }

    private async Task OpenDialogAsync(string culture = "en")
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
        await page.Find("[data-testid=gradebook-import]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=import-file]");
    }

    private Task UploadAsync(string name)
    {
        var upload = _dialogs.FindComponent<InputFile>();
        return _dialogs.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[16], name)));
    }

    [Fact]
    public async Task Accepts_xlsx_only_before_upload()
    {
        await OpenDialogAsync();

        await UploadAsync("grades.csv");

        Assert.Contains("Only Excel files", _dialogs.WaitForElement("[data-testid=import-error]").TextContent);
        Assert.Empty(_dialogs.FindAll("[data-testid=import-preview]")); // refused on the device, nothing reached the server
    }

    [Fact]
    public async Task A_file_over_the_size_cap_shows_the_message_and_uploads_nothing()
    {
        await OpenDialogAsync();
        var upload = _dialogs.FindComponent<InputFile>();

        await _dialogs.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[11 * 1024 * 1024], "big.xlsx")));

        Assert.Contains("too large", _dialogs.WaitForElement("[data-testid=import-error]").TextContent);
        Assert.Empty(_dialogs.FindAll("[data-testid=import-preview]"));
    }

    [Fact]
    public async Task Only_manual_items_can_be_picked()
    {
        await OpenDialogAsync();

        var items = _dialogs.FindComponent<GradeColumnImportDialog>().Instance.Items;

        Assert.Equal([FakeGradebookApi.ManualItemId], items.Select(i => i.Id)); // the assignment item is event-derived and left out
    }

    [Fact]
    public async Task Preview_shows_counts_unknown_codes_out_of_range_and_overwrites()
    {
        _api.ShapeImportPreview = p => p with
        {
            TotalRows = 12,
            Overwrites = 3,
            UnknownCodes = ["S-9", "S-10"],
            OutOfRange = [new GradeImportRowDto(4, "S-1", 120m, false, "grade_out_of_range")],
        };
        await OpenDialogAsync();

        await UploadAsync("grades.xlsx");

        Assert.Contains("12 rows", _dialogs.WaitForElement("[data-testid=import-counts]").TextContent);
        Assert.Contains("3 would replace", _dialogs.Find("[data-testid=import-counts]").TextContent);
        Assert.Contains("S-9, S-10", _dialogs.Find("[data-testid=import-unknown]").TextContent);
        Assert.Contains("120", _dialogs.Find("[data-testid=import-out-of-range]").TextContent);
    }

    [Fact]
    public async Task Preview_says_event_scores_are_untouched()
    {
        await OpenDialogAsync();

        await UploadAsync("grades.xlsx");

        Assert.Contains("assignments and quizzes are not changed", _dialogs.WaitForElement("[data-testid=import-event-note]").TextContent);
    }

    [Fact]
    public async Task Apply_uses_the_preview_token()
    {
        await OpenDialogAsync();
        await UploadAsync("grades.xlsx");
        _dialogs.WaitForElement("[data-testid=import-apply]");

        await _dialogs.Find("[data-testid=import-apply]").ClickAsync(new());

        _dialogs.WaitForElement("[data-testid=import-done-text]");
        Assert.False(string.IsNullOrEmpty(_api.LastAppliedToken));
        Assert.Contains("scores written", _dialogs.Find("[data-testid=import-done-text]").TextContent);
    }

    [Fact]
    public async Task Expired_preview_asks_to_upload_again()
    {
        await OpenDialogAsync();
        await UploadAsync("grades.xlsx");
        _dialogs.WaitForElement("[data-testid=import-apply]");
        _api.ExpireImportToken = true;

        await _dialogs.Find("[data-testid=import-apply]").ClickAsync(new());

        Assert.Contains("Upload the file again", _dialogs.WaitForElement("[data-testid=import-error]").TextContent);
        Assert.Empty(_dialogs.FindAll("[data-testid=import-apply]")); // the preview is gone; a new file is needed
    }

    [Fact]
    public async Task Export_downloads_the_server_file()
    {
        await OpenDialogAsync();

        await _dialogs.Find("[data-testid=import-export]").ClickAsync(new());

        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "lmsDownloadFile");
        Assert.EndsWith(".xlsx", (string)call.Arguments[0]!);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        await OpenDialogAsync("ar");

        Assert.Contains("تصدير", _dialogs.Find("[data-testid=import-export]").TextContent);
        Assert.Contains("xlsx", _dialogs.Markup); // the Arabic hint names the format
    }
}
