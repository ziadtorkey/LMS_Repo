using System.Globalization;
using Bunit;
using Lms.Client.Pages.Access;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.IdentityProviders;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Access;

public sealed class IdentityProvidersPageTests : ClientTestContext
{
    private readonly FakeIdentityProvidersApi _providers = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private static readonly Guid ExistingId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    public IdentityProvidersPageTests() => Services.AddSingleton<IIdentityProvidersApi>(_providers);

    private new async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private async Task<IRenderedComponent<IdentityProvidersPage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<IdentityProvidersPage>();
    }

    private async Task OpenEditAsync(IRenderedComponent<IdentityProvidersPage> page)
    {
        // The click handler awaits the dialog result, so the click itself is not awaited.
        _ = page.WaitForElement($"[data-testid=idp-edit-{ExistingId}]").ClickAsync(new());
        await _dialogs.WaitForElementAsync("[data-testid=dialog-save]");
    }

    private IRenderedComponent<MudSelect<string>> Select(string testId) =>
        _dialogs.FindComponents<MudSelect<string>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == testId);

    [Fact]
    public async Task Azure_kind_shows_tenant_and_hides_authority()
    {
        var page = await RenderPageAsync(Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage);
        _ = page.WaitForElement("[data-testid=idp-new]").ClickAsync(new());
        await _dialogs.WaitForElementAsync("[data-testid=idp-kind]");

        Assert.NotEmpty(_dialogs.FindAll("input[data-testid=idp-authority]"));
        Assert.Empty(_dialogs.FindAll("input[data-testid=idp-tenant]"));

        var kind = Select("idp-kind");
        await kind.InvokeAsync(() => kind.Instance.ValueChanged.InvokeAsync(IdentityProviderKinds.AzureAd));

        _dialogs.WaitForElement("input[data-testid=idp-tenant]");
        Assert.Empty(_dialogs.FindAll("input[data-testid=idp-authority]"));
    }

    [Fact]
    public async Task Secret_field_is_empty_on_edit_and_null_is_sent_when_untouched()
    {
        var page = await RenderPageAsync(Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage);
        await OpenEditAsync(page);

        Assert.Equal("", _dialogs.Find("input[data-testid=idp-secret]").GetAttribute("value") ?? "");
        Assert.Equal("password", _dialogs.Find("input[data-testid=idp-secret]").GetAttribute("type"));

        await _dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        await _dialogs.WaitForAssertionAsync(() => Assert.Single(_providers.Saved));
        Assert.Null(_providers.Saved[0].ClientSecret);

        // Typing a new secret sends it.
        await OpenEditAsync(page);
        _dialogs.Find("input[data-testid=idp-secret]").Change("new-secret");
        await _dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        await _dialogs.WaitForAssertionAsync(() => Assert.Equal(2, _providers.Saved.Count));
        Assert.Equal("new-secret", _providers.Saved[1].ClientSecret);
    }

    [Fact]
    public async Task Test_connection_shows_result()
    {
        Culture.Current = new CultureInfo("en");
        var page = await RenderPageAsync(Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage);
        await OpenEditAsync(page);

        await _dialogs.Find("[data-testid=idp-test]").ClickAsync(new());

        var result = await _dialogs.WaitForElementAsync("[data-testid=idp-test-result]");
        Assert.Contains("Connected to https://login.microsoftonline.com/", result.TextContent);
        Assert.Equal([ExistingId], _providers.Tested);
    }

    [Fact]
    public async Task Page_hidden_without_IdentityProviders_View()
    {
        Culture.Current = new CultureInfo("en");
        var page = await RenderPageAsync(Permissions.Staff.View);

        page.WaitForAssertion(() => Assert.Contains("You do not have permission", page.Markup));
        Assert.Empty(page.FindAll("[data-testid=idp-title]"));
    }

    [Fact]
    public async Task Manage_controls_are_hidden_with_view_only()
    {
        var page = await RenderPageAsync(Permissions.IdentityProviders.View);

        page.WaitForElement("[data-testid=idp-title]");
        Assert.Empty(page.FindAll("[data-testid=idp-new]"));
        Assert.Empty(page.FindAll($"[data-testid=idp-edit-{ExistingId}]"));
    }

    [Fact]
    public async Task Url_inputs_are_ltr_and_page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        Render<MudPopoverProvider>();
        await GrantAsync(Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage);
        _dialogs = Render<MudDialogProvider>();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<IdentityProvidersPage>());

        Assert.Contains("مزودو تسجيل الدخول الموحد", rtl.WaitForElement("[data-testid=idp-title]").TextContent);
        _ = rtl.WaitForElement($"[data-testid=idp-edit-{ExistingId}]").ClickAsync(new());
        await _dialogs.WaitForElementAsync("[data-testid=dialog-save]");

        foreach (var id in new[] { "idp-slug", "idp-client-id", "idp-secret", "idp-scopes", "idp-callback" })
            Assert.Equal("ltr", _dialogs.Find($"input[data-testid={id}]").GetAttribute("dir"));
        Assert.Equal("ltr", _dialogs.Find("input[data-testid=idp-tenant]").GetAttribute("dir"));
    }
}
