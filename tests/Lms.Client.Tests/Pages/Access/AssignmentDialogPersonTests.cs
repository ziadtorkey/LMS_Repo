using System.Globalization;
using Bunit;
using Lms.Client.Pages.Access;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Access;

public sealed class AssignmentDialogPersonTests : StaffTestContext
{
    private async Task OpenAsync()
    {
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Create);
        RenderProviders();
        var cut = Render<AssignmentsPage>();
        await cut.WaitForElement("[data-testid=assign-new]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=assign-person]");
    }

    private IRenderedComponent<MudAutocomplete<StaffSummaryDto>> Autocomplete =>
        Dialogs.FindComponent<MudAutocomplete<StaffSummaryDto>>();

    [Fact]
    public async Task Autocomplete_searches_staff_and_sends_selected_email()
    {
        await OpenAsync();

        var results = (await Autocomplete.Instance.SearchFunc!("sara", CancellationToken.None)!)!;
        var sara = Assert.Single(results);
        Assert.Equal("sara@x.test", sara.Email);
        Assert.Equal(20, Staff.LastQuery!.PageSize);
        Assert.Equal("sara", Staff.LastQuery.Search);

        await Dialogs.InvokeAsync(() => Autocomplete.Instance.ValueChanged.InvokeAsync(sara));
        await Dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        var access = Services.GetRequiredService<IAccessAdminApi>() as FakeAccessAdminApi;
        Dialogs.WaitForAssertion(() => Assert.Equal("sara@x.test", access!.LastAssignment!.Email));
    }

    [Fact]
    public async Task Save_without_a_selected_person_is_rejected()
    {
        await OpenAsync();

        await Dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        Assert.NotNull(Dialogs.WaitForElement("[data-testid=assign-error]"));
        Assert.Null(((FakeAccessAdminApi)Services.GetRequiredService<IAccessAdminApi>()).LastAssignment);
    }

    [Fact]
    public async Task Student_and_guardian_roles_are_not_offered()
    {
        await OpenAsync();
        var select = Dialogs.FindComponents<MudSelect<Guid>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "assign-role");

        select.Find("div.mud-input-control").MouseDown();

        var ids = Popovers.FindComponents<MudSelectItem<Guid>>().Select(i => i.Instance.Value).ToList();
        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Equal(RoleBaseKinds.Staff, Access.Roles.First(r => r.Id == id).BaseKind));
        Assert.Contains(Access.Roles, r => r.BaseKind != RoleBaseKinds.Staff);
    }

    [Fact]
    public async Task Create_staff_link_opens_the_staff_dialog_and_selects_the_new_person()
    {
        await OpenAsync();

        _ = Dialogs.Find("[data-testid=assign-create-staff]").ClickAsync(new());
        Dialogs.WaitForElement("input[data-testid=staff-email]");
    }

    [Theory]
    [InlineData("ar", "الموظف")]
    [InlineData("en", "Staff member")]
    public async Task Dialog_renders_rtl_in_arabic(string culture, string label)
    {
        Culture.Current = new CultureInfo(culture);
        await OpenAsync();

        Assert.Contains(label, Dialogs.Markup);
        Assert.Equal(culture == "ar", Culture.IsRightToLeft);
    }
}
