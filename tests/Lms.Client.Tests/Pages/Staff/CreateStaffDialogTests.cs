using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Staff;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Staff;

public sealed class CreateStaffDialogTests : StaffTestContext
{
    private async Task<IRenderedComponent<StaffListPage>> OpenDialogAsync()
    {
        await GrantAsync(Permissions.Staff.View, Permissions.Staff.Create);
        RenderProviders();
        var page = Render<StaffListPage>();
        // The click handler awaits the dialog result, so do not await the click itself.
        _ = page.Find("[data-testid=staff-new]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=dialog-save]");
        return page;
    }

    private void Type(string id, string value) => Dialogs.Find($"input[data-testid={id}]").Change(value);

    private async Task FillValidAsync()
    {
        Type("staff-name-ar", "ليلى"); Type("staff-name-en", "Laila");
        Type("staff-email", "laila@x.test");
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        await ChooseAsync(SelectWithTestId(Dialogs, "staff-role-0"), (Guid?)RoleOfKind(Lms.Shared.Authorization.RoleBaseKinds.Staff));
        var roleUnit = Dialogs.FindComponents<UnitPicker>().First(u => u.Instance.TestId == "staff-role-unit-0");
        await Dialogs.InvokeAsync(() => roleUnit.Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
    }

    [Fact]
    public async Task Create_requires_both_names_email_and_one_role()
    {
        await OpenDialogAsync();

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Assert.Null(Staff.LastCreate);

        Type("staff-name-ar", "ليلى"); Type("staff-name-en", "Laila"); Type("staff-email", "laila@x.test");
        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<UnitPicker>().Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        Assert.Null(Staff.LastCreate);
        Dialogs.WaitForAssertion(() => Assert.NotNull(Dialogs.Find("[data-testid=dialog-error]")));
    }

    [Fact]
    public async Task Valid_form_posts_the_request_and_closes()
    {
        await OpenDialogAsync();
        await FillValidAsync();

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.NotNull(Staff.LastCreate));
        var sent = Staff.LastCreate!;
        Assert.Equal("Laila", sent.NameEn);
        Assert.Equal("laila@x.test", sent.Email);
        Assert.Equal(FakeUnits.SchoolId, sent.HomeOrgUnitId);
        Assert.Single(sent.Roles);
    }

    [Fact]
    public async Task Role_select_lists_staff_kind_roles_only()
    {
        await OpenDialogAsync();
        var select = SelectWithTestId(Dialogs, "staff-role-0");

        // Open the select so its items render in the popover.
        select.Find("div.mud-input-control").MouseDown();
        var staffNames = Access.Roles.Where(r => r.BaseKind == RoleBaseKinds.Staff).Select(r => r.NameAr).ToList();
        var otherNames = Access.Roles.Where(r => r.BaseKind != RoleBaseKinds.Staff).Select(r => r.NameAr).ToList();
        Assert.NotEmpty(staffNames);
        Assert.NotEmpty(otherNames);

        var items = Popovers.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value).ToList();
        Assert.Equal(staffNames.Count, items.Count);
        Assert.All(items, id => Assert.Equal(RoleBaseKinds.Staff, Access.Roles.First(r => r.Id == id).BaseKind));
    }

    [Fact]
    public async Task Duplicate_email_shows_field_specific_message()
    {
        Culture.Current = new CultureInfo("en");
        await OpenDialogAsync();
        await FillValidAsync();
        Type("staff-email", "sara@x.test");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() =>
            Assert.Contains("already uses this email", Dialogs.Find("[data-testid=dialog-error]").TextContent));
    }

    [Fact]
    public async Task Email_mobile_national_id_inputs_are_ltr_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await OpenDialogAsync();

        foreach (var id in new[] { "staff-email", "staff-mobile", "staff-national-id" })
            Assert.Equal("ltr", Dialogs.Find($"input[data-testid={id}]").GetAttribute("dir"));
        Assert.Equal("rtl", Dialogs.Find("input[data-testid=staff-name-ar]").GetAttribute("dir"));
    }
}
