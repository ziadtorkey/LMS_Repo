using Bunit;
using Lms.Client.Layout;
using Lms.Client.Tests.Support;
using System.Globalization;

namespace Lms.Client.Tests.Layout;

public sealed class MainLayoutTests : ClientTestContext
{
    private IRenderedComponent<MainLayout> RenderLayout(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        CultureInfo.CurrentUICulture = Culture.Current;
        return Render<MainLayout>(p => p.Add(l => l.Body, b => b.AddMarkupContent(0, "<p>body</p>")));
    }

    [Fact]
    public void MainLayout_is_rtl_when_culture_is_ar()
    {
        var cut = RenderLayout("ar");
        Assert.Equal("rtl", cut.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("mud-application-layout-rtl", cut.Markup);
    }

    [Fact]
    public void MainLayout_is_ltr_when_culture_is_en()
    {
        var cut = RenderLayout("en");
        Assert.Equal("ltr", cut.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.DoesNotContain("mud-application-layout-rtl", cut.Markup);
    }

    [Fact]
    public async Task Language_switch_offers_the_other_language()
    {
        var cut = RenderLayout("ar");
        Assert.Contains("English", cut.Find("[data-testid=lang-switch]").TextContent);
        await cut.Find("[data-testid=lang-switch]").ClickAsync(new());
        Assert.Equal("en", Culture.LastSet);
    }

    [Fact]
    public void Header_exposes_user_menu_and_context_switcher_for_authenticated_user()
    {
        var cut = RenderLayout("en");
        Assert.NotNull(cut.Find("[data-testid=user-menu]"));
        Assert.NotNull(cut.Find("[data-testid=context-switcher]"));
    }

    [Theory]
    [InlineData("ar", "نظام إدارة التعلم")]
    [InlineData("en", "Learning Management System")]
    public void Header_text_is_localized(string culture, string expected) =>
        Assert.Contains(expected, RenderLayout(culture).Markup);
}
