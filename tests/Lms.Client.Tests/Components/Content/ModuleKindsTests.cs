using Bunit;
using Lms.Client.Components.Content;
using Lms.Client.Components.Content.Kinds;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Content;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components.Content;

public sealed class ModuleKindsTests : ClientTestContext
{
    public ModuleKindsTests()
    {
        Services.AddSingleton<IModulesApi>(new FakeModulesApi());
        Services.AddSingleton<IAssetsApi>(new FakeAssetsApi());
    }

    [Theory]
    [InlineData("https://example.test/a", true)]
    [InlineData("http://example.test", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,<script>1</script>", false)]
    [InlineData("//example.test", false)]
    [InlineData("/relative", false)]
    [InlineData("ftp://example.test", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_absolute_http_and_https_links_are_allowed(string? url, bool allowed) =>
        Assert.Equal(allowed, ModuleLinks.IsAllowed(url));

    [Fact]
    public void Link_rejects_javascript_scheme()
    {
        var model = new ModuleFormModel();
        var cut = Render<LinkEditor>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid=module-url]").Input("javascript:alert(1)");

        Assert.Contains("http", cut.Markup);
        Assert.Contains("mud-input-error", cut.Markup);
    }

    [Fact]
    public void Link_accepts_https()
    {
        var model = new ModuleFormModel();
        var cut = Render<LinkEditor>(p => p.Add(x => x.Model, model));

        cut.Find("[data-testid=module-url]").Input("https://example.test");

        Assert.Equal("https://example.test", model.Url);
        Assert.DoesNotContain("mud-input-error", cut.Markup);
    }

    [Fact]
    public async Task Dialog_refuses_to_save_a_blocked_link()
    {
        await GrantAsync();
        Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        _ = Services.GetRequiredService<IDialogService>().ShowAsync<ModuleDialog>("t", new DialogParameters<ModuleDialog>
        {
            { x => x.ClassCourseId, Guid.NewGuid() }, { x => x.LessonKey, Guid.NewGuid() }, { x => x.Kind, ModuleKinds.Link },
        });
        dialogs.WaitForAssertion(() => Assert.NotNull(dialogs.Find("[data-testid=module-save]")));
        dialogs.Find("[data-testid=module-title-ar]").Change("ر");
        dialogs.Find("[data-testid=module-title-en]").Change("Link");
        dialogs.Find("[data-testid=module-url]").Input("javascript:alert(1)");

        dialogs.Find("[data-testid=module-save]").Click();

        dialogs.WaitForAssertion(() => Assert.NotNull(dialogs.Find("[data-testid=module-error]")));
    }
}
