using System.Globalization;
using Bunit;
using Lms.Client.Navigation;
using Lms.Client.Pages.Me;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Me;

public sealed class MyChildrenPageTests : StudentsTestContext
{
    private async Task<IRenderedComponent<MyChildrenPage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        return Render<MyChildrenPage>();
    }

    [Fact]
    public async Task Shows_one_card_per_child()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.ViewChildren);

        cut.WaitForElement("[data-testid=children-grid]");
        var cards = cut.FindAll("[data-testid^=child-card-]");
        Assert.Equal(GuardiansApi.MyChildren.Count, cards.Count);
        Assert.Contains("Ahmed Saeed", cards[0].TextContent);
        Assert.Contains("S-1001", cards[0].TextContent);
        Assert.Contains("Main School", cards[0].TextContent);
        Assert.Contains("Mother", cards[0].TextContent);
    }

    [Fact]
    public async Task Empty_state_and_context_hint()
    {
        Culture.Current = new CultureInfo("en");
        GuardiansApi.MyChildren.Clear();
        var cut = await RenderPageAsync(Permissions.Students.ViewChildren);

        Assert.Contains("No children are linked to your account in this school.", cut.WaitForElement("[data-testid=children-empty]").TextContent);
        Assert.Contains("switch to that school", cut.Find("[data-testid=children-hint]").TextContent);
    }

    [Fact]
    public async Task Page_is_forbidden_without_Students_ViewChildren()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.View);

        cut.WaitForAssertion(() => Assert.Contains("You do not have permission", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=children-title]"));
    }

    [Fact]
    public async Task Home_shows_the_children_card_only_for_guardians()
    {
        await GrantAsync(Permissions.Students.View);
        var plain = Render<Lms.Client.Pages.Home>();
        plain.WaitForAssertion(() => Assert.Empty(plain.FindAll("[data-testid=api-error]")));
        Assert.Empty(plain.FindAll("[data-testid=home-children-card]"));

        await GrantAsync(Permissions.Students.ViewChildren);
        var guardian = Render<Lms.Client.Pages.Home>();
        Assert.EndsWith("me/children", guardian.WaitForElement("[data-testid=home-children-link]").GetAttribute("href"));
    }

    [Fact]
    public void Nav_item_only_for_Students_ViewChildren()
    {
        var item = NavCatalog.Items.Single(i => i.ResourceKey == "Nav_MyChildren");

        Assert.Equal("me/children", item.Href);
        Assert.Equal(Permissions.Students.ViewChildren, item.RequiredPermission);
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Students.ViewChildren);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<MyChildrenPage>());

        Assert.Contains("أبنائي", rtl.WaitForElement("[data-testid=children-title]").TextContent);
        var card = rtl.WaitForElement("[data-testid^=child-card-]");
        Assert.Contains("أحمد سعيد", card.TextContent);
        Assert.Contains("الأم", card.TextContent);
        Assert.Contains("S-1001", card.TextContent);
    }
}
