using System.Globalization;
using Bunit;
using Lms.Client.Components.Imports;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Imports;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Imports;

public sealed class ImportWizardTests : ClientTestContext
{
    private readonly StubImportsApi _imports = new();
    private readonly Guid _unitId = Guid.NewGuid();
    private ImportBatchDto? _committed;
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;

    public ImportWizardTests()
    {
        Services.AddSingleton<IImportsApi>(_imports);
        var unit = new OrgUnitNodeDto(_unitId, null, Guid.NewGuid(), "مدرسة", "School", "SCH", 0, "Active", false, null, false, false, []);
        Services.AddSingleton<IUnitLookupApi>(new SingleUnit(unit));
    }

    private IRenderedComponent<ImportWizard> RenderWizard()
    {
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<ImportWizard>(p => p
            .Add(w => w.Kind, ImportKinds.Staff)
            .Add(w => w.PollInterval, TimeSpan.FromMilliseconds(10))
            .Add(w => w.OnCommitted, EventCallback.Factory.Create<ImportBatchDto>(this, b => _committed = b)));
    }

    private static async Task ChooseFileAsync(IRenderedComponent<ImportWizard> cut, string name, int bytes = 8)
    {
        var upload = cut.FindComponent<InputFile>();
        await cut.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name)));
    }

    private async Task ToReviewAsync(IRenderedComponent<ImportWizard> cut)
    {
        await cut.WaitForElement("[data-testid=import-next]").ClickAsync(new());
        await ChooseFileAsync(cut, "staff.xlsx");
        await cut.WaitForElement("[data-testid=import-upload]:not([disabled])").ClickAsync(new());
    }

    [Fact]
    public async Task Template_download_uses_selected_language()
    {
        Culture.Current = new CultureInfo("ar");
        var cut = RenderWizard();

        await cut.WaitForElement("[data-testid=import-template]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(["ar"], _imports.TemplateCultures));

        await cut.Find("input[data-testid=import-lang-en], [data-testid=import-lang-en] input").ClickAsync(new());
        await cut.Find("[data-testid=import-template]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal(["ar", "en"], _imports.TemplateCultures));
        JSInterop.VerifyInvoke("lmsDownloadFile", 2);
    }

    [Fact]
    public async Task Oversized_file_is_rejected_before_upload()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderWizard();
        await cut.WaitForElement("[data-testid=import-next]").ClickAsync(new());

        await ChooseFileAsync(cut, "big.xlsx", ImportLimits.MaxFileBytes + 1);

        Assert.Contains("larger than 10 MB", cut.WaitForElement("[data-testid=import-file-error]").TextContent);
        Assert.True(cut.Find("[data-testid=import-upload]").HasAttribute("disabled"));
        Assert.Empty(_imports.Uploads);
    }

    [Fact]
    public async Task Polling_stops_when_batch_is_Valid_and_commit_is_enabled()
    {
        _imports.Statuses = [ImportStatuses.Validating, ImportStatuses.Valid];
        var cut = RenderWizard();

        await ToReviewAsync(cut);

        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=import-commit]").HasAttribute("disabled")), TimeSpan.FromSeconds(5));
        Assert.Equal((_unitId, "staff.xlsx"), Assert.Single(_imports.Uploads));
        var reads = _imports.BatchReads;
        await Task.Delay(120);
        Assert.Equal(reads, _imports.BatchReads); // no more polling once the batch is Valid
    }

    [Fact]
    public async Task Invalid_batch_shows_blocked_message_and_error_report_button_and_no_commit()
    {
        _imports.Statuses = [ImportStatuses.Invalid];
        _imports.ErrorCount = 2;
        var cut = RenderWizard();

        await ToReviewAsync(cut);

        cut.WaitForElement("[data-testid=import-blocked]");
        Assert.Empty(cut.FindAll("[data-testid=import-commit]"));
        await cut.Find("[data-testid=import-error-report]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(1, _imports.ErrorReports));
    }

    [Fact]
    public async Task Commit_requires_confirmation_and_raises_OnCommitted()
    {
        _imports.Statuses = [ImportStatuses.Valid];
        var cut = RenderWizard();
        await ToReviewAsync(cut);

        // Declining the confirmation commits nothing.
        await cut.WaitForElement("[data-testid=import-commit]").ClickAsync(new());
        await _dialogs.WaitForElement("[data-testid=confirm-no]").ClickAsync(new());
        _dialogs.WaitForAssertion(() => Assert.Empty(_dialogs.FindAll("[data-testid=confirm-no]")));
        Assert.Empty(_imports.Committed);

        await cut.Find("[data-testid=import-commit]").ClickAsync(new());
        await _dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.NotNull(_committed), TimeSpan.FromSeconds(5));
        Assert.Equal(_imports.BatchId, Assert.Single(_imports.Committed));
        Assert.Equal(ImportStatuses.Committed, _committed!.Status);
        await cut.WaitForElementAsync("[data-testid=import-done]");
    }

    [Fact]
    public async Task Wizard_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        Render<MudPopoverProvider>();
        var cut = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<ImportWizard>(w =>
            w.Add(x => x.Kind, ImportKinds.Staff)));

        Assert.Contains("الاستيراد من Excel", cut.WaitForElement("[data-testid=import-wizard]").TextContent);
        await cut.Find("[data-testid=import-next]").ClickAsync(new());
        // The file name stays LTR whatever the page direction.
        await ChooseFileAsync(cut.FindComponent<ImportWizard>(), "staff.xlsx");
        Assert.Equal("ltr", cut.WaitForElement("[data-testid=import-file-name]").GetAttribute("dir"));
    }

    private sealed class SingleUnit(OrgUnitNodeDto unit) : IUnitLookupApi
    {
        public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrgUnitNodeDto>>([unit]);
    }
}
