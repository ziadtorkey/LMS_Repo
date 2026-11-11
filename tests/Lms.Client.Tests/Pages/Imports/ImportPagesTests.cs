using System.Globalization;
using Bunit;
using Lms.Client.Pages.Staff;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Imports;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Imports;

public sealed class ImportPagesTests : StaffTestContext
{
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(8); // the page polls every 2 s
    private readonly StubImportsApi _imports = new();

    public ImportPagesTests() => Services.AddSingleton<IImportsApi>(_imports);

    private async Task<IRenderedComponent<StaffImportPage>> RenderImportAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        return Render<StaffImportPage>();
    }

    [Fact]
    public async Task Import_button_hidden_without_Staff_Import()
    {
        await GrantAsync(Permissions.Staff.View);
        RenderProviders();
        var cut = Render<StaffListPage>();
        cut.WaitForAssertion(() => Assert.NotNull(Staff.LastQuery));
        Assert.Empty(cut.FindAll("[data-testid=staff-import]"));

        await GrantAsync(Permissions.Staff.View, Permissions.Staff.Import);
        var allowed = Render<StaffListPage>();
        Assert.NotEmpty(allowed.WaitForElements("[data-testid=staff-import]"));
    }

    [Fact]
    public async Task Page_without_Staff_Import_is_forbidden()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderImportAsync(Permissions.Staff.View);

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=import-wizard]")));
        Assert.Contains("You do not have permission", cut.Markup);
    }

    [Fact]
    public async Task Staff_page_uses_staff_kind()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderImportAsync(Permissions.Staff.Import);

        cut.WaitForElement("[data-testid=import-wizard]");
        Assert.Contains("Import staff", cut.Markup);
        await cut.Find("[data-testid=import-template]").ClickAsync(new());
        Assert.Equal(["en"], _imports.TemplateCultures);
        Assert.Equal(ImportKinds.Staff, _imports.LastKind);
    }

    [Fact]
    public async Task After_commit_navigates_to_list_of_target_unit()
    {
        _imports.Statuses = [ImportStatuses.Valid];
        var cut = await RenderImportAsync(Permissions.Staff.Import);
        var wizard = cut.WaitForElement("[data-testid=import-next]");
        await wizard.ClickAsync(new());
        var upload = cut.FindComponent<Microsoft.AspNetCore.Components.Forms.InputFile>();
        await cut.InvokeAsync(() => upload.UploadFiles(InputFileContent.CreateFromBinary(new byte[8], "staff.xlsx")));
        await cut.WaitForElement("[data-testid=import-upload]:not([disabled])").ClickAsync(new());
        await (await cut.WaitForElementAsync("[data-testid=import-commit]:not([disabled])", Slow)).ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        await cut.WaitForAssertionAsync(() => Assert.Contains($"staff?unit={_imports.UnitId}", nav.Uri), Slow);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var cut = await RenderImportAsync(Permissions.Staff.Import);

        Assert.Contains("استيراد الموظفين", cut.WaitForElement("[data-testid=import-wizard]").TextContent);
    }
}
