using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Staff;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Staff;

public sealed class StaffListPageTests : StaffTestContext
{
    private async Task<IRenderedComponent<StaffListPage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        return Render<StaffListPage>();
    }

    [Fact]
    public async Task List_shows_staff_with_role_chips_and_shared_marker()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Staff.View);

        cut.WaitForAssertion(() => Assert.Contains("Khaled Ali", cut.Markup));
        Assert.Contains("Sara Ahmed", cut.Markup);
        Assert.Contains("Teacher", cut.Markup);
        var shared = Staff.All.First(s => s.Summary.NameEn == "Khaled Ali").Summary;
        Assert.NotNull(cut.Find($"[data-testid=shared-chip-{shared.Id}]"));
        var plain = Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed").Summary;
        Assert.Empty(cut.FindAll($"[data-testid=shared-chip-{plain.Id}]"));
    }

    [Fact]
    public async Task Filters_send_unit_role_status_and_search()
    {
        var cut = await RenderPageAsync(Permissions.Staff.View);
        cut.WaitForAssertion(() => Assert.NotNull(Staff.LastQuery));
        var roleId = RoleOfKind(RoleBaseKinds.Staff);

        await cut.InvokeAsync(() => cut.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        cut.WaitForAssertion(() => Assert.Equal(FakeUnits.SchoolId, Staff.LastQuery!.OrgUnitId));

        await ChooseAsync(SelectWithTestId(cut, "staff-filter-role"), (Guid?)roleId);
        cut.WaitForAssertion(() => Assert.Equal(roleId, Staff.LastQuery!.RoleId));

        await ChooseAsync(cut.FindComponents<MudSelect<string>>().First(), PersonStatuses.Active);
        cut.WaitForAssertion(() => Assert.Equal(PersonStatuses.Active, Staff.LastQuery!.Status));

        cut.Find("input[data-testid=staff-search]").Input("sara");
        cut.WaitForAssertion(() => Assert.Equal("sara", Staff.LastQuery!.Search), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task New_button_hidden_without_Staff_Create()
    {
        var cut = await RenderPageAsync(Permissions.Staff.View);
        cut.WaitForAssertion(() => Assert.NotNull(Staff.LastQuery));
        Assert.Empty(cut.FindAll("[data-testid=staff-new]"));
    }

    [Fact]
    public async Task New_button_shown_with_Staff_Create()
    {
        var cut = await RenderPageAsync(Permissions.Staff.View, Permissions.Staff.Create);
        Assert.NotNull(cut.Find("[data-testid=staff-new]"));
    }

    [Theory]
    [InlineData("ar", "الموظفون")]
    [InlineData("en", "Staff")]
    public async Task Page_is_localized_and_renders_rtl_in_arabic(string culture, string title)
    {
        Culture.Current = new CultureInfo(culture);
        var cut = await RenderPageAsync(Permissions.Staff.View);

        Assert.Contains(title, cut.Find("[data-testid=staff-title]").TextContent);
        cut.WaitForAssertion(() => Assert.NotNull(Staff.LastQuery));
        Assert.Equal(culture == "ar", Culture.IsRightToLeft);
    }

    [Fact]
    public async Task Email_is_shown_left_to_right()
    {
        Culture.Current = new CultureInfo("ar");
        var cut = await RenderPageAsync(Permissions.Staff.View);

        cut.WaitForAssertion(() => Assert.Contains("sara@x.test", cut.Markup));
        Assert.Equal("ltr", cut.FindAll("span[dir=ltr]").First(e => e.TextContent == "sara@x.test").GetAttribute("dir"));
    }
}
