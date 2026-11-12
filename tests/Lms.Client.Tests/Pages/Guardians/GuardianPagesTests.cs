using System.Globalization;
using Bunit;
using Lms.Client.Pages.Guardians;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Guardians;

public sealed class GuardianPagesTests : StudentsTestContext
{
    private async Task GrantViewAsync(params string[] extra)
    {
        await GrantAsync([Permissions.Students.View, .. extra]);
        RenderProviders();
    }

    [Fact]
    public async Task Guardian_list_searches_and_links_to_profiles()
    {
        Culture.Current = new CultureInfo("en");
        await GrantViewAsync();
        var cut = Render<GuardianListPage>();

        cut.WaitForElement($"[data-testid=guardian-link-{FakeGuardiansApi.MonaId}]");
        Assert.Contains("Khaled", cut.Markup);

        cut.Find("input[data-testid=guardian-search]").Input("mona");
        await cut.WaitForAssertionAsync(() => Assert.Equal("mona", GuardiansApi.LastSearch), TimeSpan.FromSeconds(3));
        await cut.WaitForAssertionAsync(() => Assert.DoesNotContain("Khaled", cut.Markup));
    }

    [Fact]
    public async Task List_is_forbidden_without_Students_View()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Staff.View);
        RenderProviders();
        var cut = Render<GuardianListPage>();

        cut.WaitForAssertion(() => Assert.Contains("You do not have permission", cut.Markup));
    }

    [Fact]
    public async Task Guardian_profile_lists_children_in_scope()
    {
        Culture.Current = new CultureInfo("en");
        await GrantViewAsync();
        var cut = Render<GuardianProfilePage>(p => p.Add(x => x.Id, FakeGuardiansApi.MonaId));

        cut.WaitForElement("[data-testid=children-table]");
        Assert.Contains("Mona", cut.Find("[data-testid=profile-name]").TextContent);
        Assert.Contains("Ahmed Saeed", cut.Markup);
        Assert.Contains("Laila Hassan", cut.Markup);
        var link = cut.Find("[data-testid=child-link-00000000-0000-0000-0000-0000000000c1]");
        Assert.EndsWith("students/00000000-0000-0000-0000-0000000000c1", link.GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid=action-edit]"));
    }

    [Fact]
    public async Task Edit_guardian_saves_the_changed_data()
    {
        await GrantViewAsync(Permissions.Students.Update);
        var cut = Render<GuardianProfilePage>(p => p.Add(x => x.Id, FakeGuardiansApi.MonaId));

        _ = cut.WaitForElement("[data-testid=action-edit]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("input[data-testid=guardian-mobile]")).Change("0507777777");
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(GuardiansApi.Updates));
        Assert.Equal("0507777777", GuardiansApi.Updates[0].Request.Data.Mobile);
        Assert.Equal("29001010101", GuardiansApi.Updates[0].Request.Data.NationalId);
    }

    [Fact]
    public async Task Pages_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantViewAsync();
        var list = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<GuardianListPage>());
        Assert.Contains("أولياء الأمور", list.WaitForElement("[data-testid=guardians-title]").TextContent);

        var profile = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true)
            .AddChildContent<GuardianProfilePage>(c => c.Add(x => x.Id, FakeGuardiansApi.MonaId)));
        Assert.Contains("منى", profile.WaitForElement("[data-testid=profile-name]").TextContent);
        Assert.Equal("ltr", profile.Find("[data-testid=profile-mobile] span").GetAttribute("dir"));
    }
}
