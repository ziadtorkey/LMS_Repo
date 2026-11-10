using System.Globalization;
using Bunit;
using Lms.Client.Pages.Clients;
using Lms.Client.Tests.Support;
using Lms.Shared.Organizations;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Clients;

public sealed class ClientsPageTests : ClientTestContext
{
    private static ClientDto Row(string en, string ar, string status = "Active") =>
        new(Guid.NewGuid(), ar, en, OrganizationTypes.SchoolGroup, status, Guid.NewGuid(), DateTime.UtcNow);

    private (IRenderedComponent<ClientsPage> Page, IRenderedComponent<MudDialogProvider> Dialogs) RenderPage()
    {
        Api.Me = Api.Me with { IsPlatformAdmin = true };
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        return (Render<ClientsPage>(), dialogs);
    }

    [Fact]
    public void Clients_page_lists_and_searches_clients()
    {
        Culture.Current = new CultureInfo("en");
        Clients.Rows.AddRange([Row("Al Noor", "النور"), Row("Horizon", "الأفق")]);
        var (page, _) = RenderPage();
        page.WaitForAssertion(() => Assert.Contains("Horizon", page.Markup));

        page.Find("input[data-testid=clients-search]").Input("Noor");
        page.Find("[data-testid=clients-search-button]").Click();

        page.WaitForAssertion(() => Assert.DoesNotContain("Horizon", page.Markup));
        Assert.Contains("Noor", Clients.Searches);
    }

    [Fact]
    public async Task Create_client_dialog_validates_required_fields_and_posts_request()
    {
        var (page, dialogs) = RenderPage();
        // The click handler awaits the dialog result, so do not await the click itself.
        _ = page.Find("[data-testid=clients-new]").ClickAsync(new());
        dialogs.WaitForElement("[data-testid=dialog-save]");

        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Assert.Empty(Clients.Created);

        void Type(string id, string value) => dialogs.Find($"input[data-testid={id}]").Change(value);
        Type("client-name-ar", "مدارس الأمل"); Type("client-name-en", "Hope Schools");
        Type("root-level-ar", "المجموعة"); Type("root-level-en", "Group");
        Type("root-code", "HOPE");
        Type("admin-email", "admin@hope.test");
        Type("admin-name-ar", "المسؤول"); Type("admin-name-en", "Admin");
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Single(Clients.Created));
        var sent = Clients.Created[0];
        Assert.Equal("Hope Schools", sent.NameEn);
        Assert.Equal("HOPE", sent.RootUnitCode);
        Assert.Equal("admin@hope.test", sent.AdminEmail);
        Assert.Equal(OrganizationTypes.SchoolGroup, sent.Type);
    }

    [Fact]
    public async Task Suspending_a_client_sends_an_update_with_the_new_status()
    {
        var row = Row("Al Noor", "النور");
        Clients.Rows.Add(row);
        var (page, _) = RenderPage();
        page.WaitForElement($"[data-testid=client-toggle-{row.Id}]");

        await page.Find($"[data-testid=client-toggle-{row.Id}]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal("Suspended", Assert.Single(Clients.Updated).Request.Status));
    }

    [Theory]
    [InlineData("ar", "العملاء")]
    [InlineData("en", "Clients")]
    public void Page_is_localized(string culture, string title)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var (page, _) = RenderPage();
        Assert.Contains(title, page.Find("[data-testid=clients-title]").TextContent);
    }
}
