using System.Globalization;
using Bunit;
using Lms.Client.Layout;
using Lms.Client.Pages.Organization;
using Lms.Client.Pages.Templates;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Organizations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Templates;

public sealed class TemplatesPageTests : ClientTestContext
{
    private readonly OrgTemplateSummaryDto _template = new(Guid.NewGuid(), "قالب مدرسة", "School template", TemplateVisibilities.Shared,
        Guid.NewGuid(), "مدارس", "Schools", 3, ["Year"], DateTime.UtcNow);

    private async Task<(IRenderedComponent<TemplatesPage> Page, IRenderedComponent<MudDialogProvider> Dialogs)> RenderPageAsync(params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        Templates.Items.Add(_template);
        await GrantAsync(permissions);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var page = Render<TemplatesPage>();
        page.WaitForElement($"[data-testid=template-row-{_template.Id}]");
        return (page, dialogs);
    }

    [Fact]
    public async Task Templates_page_hides_delete_without_permission()
    {
        var (page, _) = await RenderPageAsync(Permissions.Templates.View);

        Assert.Empty(page.FindAll($"[data-testid=template-delete-{_template.Id}]"));
        Assert.Empty(page.FindAll($"[data-testid=template-apply-{_template.Id}]"));
    }

    [Fact]
    public async Task Templates_page_shows_actions_with_permissions_and_deletes_after_confirm()
    {
        var (page, dialogs) = await RenderPageAsync(Permissions.Templates.View, Permissions.Templates.Delete, Permissions.Templates.Apply);

        Assert.NotEmpty(page.FindAll($"[data-testid=template-apply-{_template.Id}]"));
        _ = page.Find($"[data-testid=template-delete-{_template.Id}]").ClickAsync(new());
        dialogs.WaitForElement("[data-testid=confirm-yes]");
        await dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.Equal(_template.Id, Assert.Single(Templates.Deleted)));
    }

    [Fact]
    public async Task Stale_context_while_deleting_goes_to_the_chooser()
    {
        var (page, dialogs) = await RenderPageAsync(Permissions.Templates.View, Permissions.Templates.Delete);
        Templates.DeleteFailure = new Lms.Client.Services.Api.ApiContextException("invalid_context");

        _ = page.Find($"[data-testid=template-delete-{_template.Id}]").ClickAsync(new());
        dialogs.WaitForElement("[data-testid=confirm-yes]");
        await dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        page.WaitForAssertion(() => Assert.EndsWith("/choose-context", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Templates.Items.Add(_template);
        await GrantAsync(Permissions.Templates.View);
        Culture.Current = new CultureInfo("ar");

        var layout = Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => { b.OpenComponent<TemplatesPage>(0); b.CloseComponent(); })));

        layout.WaitForElement($"[data-testid=template-row-{_template.Id}]");
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("قالب مدرسة", layout.Find($"[data-testid=template-row-{_template.Id}]").TextContent);
        Assert.Contains("القوالب", layout.Find("[data-testid=templates-title]").TextContent);
    }

    [Fact]
    public async Task Save_template_sends_parameterize_map()
    {
        Culture.Current = new CultureInfo("en");
        var unit = new OrgUnitNodeDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "مدرسة 2026", "School 2026", "S-2026", 1, "Active", false, null, false, false, []);
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<SaveTemplateDialog>("Save", new DialogParameters<SaveTemplateDialog> { { x => x.Source, unit } });
        dialogs.WaitForElement("[data-testid=dialog-save]");

        dialogs.Find("input[data-testid=template-name-ar]").Change("قالب");
        dialogs.Find("input[data-testid=template-name-en]").Change("Template");
        await dialogs.Find("[data-testid=template-add-replacement]").ClickAsync(new());
        dialogs.WaitForElement("input[data-testid=replace-literal-0]");
        dialogs.Find("input[data-testid=replace-literal-0]").Change("2026");
        dialogs.Find("input[data-testid=replace-token-0]").Change("Year");
        await dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        dialogs.WaitForAssertion(() => Assert.Single(Templates.Saved));
        var sent = Templates.Saved[0];
        Assert.Equal(unit.Id, sent.SourceOrgUnitId);
        Assert.Equal("{Year}", sent.Parameterize["2026"]);
        Assert.Equal(TemplateVisibilities.Private, sent.Visibility);
    }
}
