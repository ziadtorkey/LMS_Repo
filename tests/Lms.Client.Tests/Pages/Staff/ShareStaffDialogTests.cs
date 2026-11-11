using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Staff;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Staff;

public sealed class ShareStaffDialogTests : StaffTestContext
{
    private async Task<Guid> OpenAsync()
    {
        await GrantAsync(Permissions.Staff.View, Permissions.Staff.Update, Permissions.Assignments.Create);
        RenderProviders();
        var sara = Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed").Summary.Id;
        var cut = Render<StaffProfilePage>(p => p.Add(x => x.Id, sara));
        cut.WaitForElement("[data-testid=action-share]");
        _ = cut.Find("[data-testid=action-share]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=share-save]");
        return sara;
    }

    private async Task FillRowAsync()
    {
        await ChooseAsync(SelectWithTestId(Dialogs, "staff-role-0"), (Guid?)RoleOfKind(RoleBaseKinds.Staff));
        var unit = Dialogs.FindComponents<UnitPicker>().First(u => u.Instance.TestId == "staff-role-unit-0");
        await Dialogs.InvokeAsync(() => unit.Instance.ValueChanged.InvokeAsync(FakeUnits.GroupId));
    }

    [Fact]
    public async Task Share_dialog_sends_targets()
    {
        var sara = await OpenAsync();
        await FillRowAsync();

        await Dialogs.Find("[data-testid=share-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Single(Staff.Shares));
        var (id, request) = Staff.Shares[0];
        Assert.Equal(sara, id);
        var target = Assert.Single(request.Targets);
        Assert.Equal(FakeUnits.GroupId, target.OrgUnitId);
    }

    [Fact]
    public async Task Share_dialog_requires_a_complete_row()
    {
        await OpenAsync();

        await Dialogs.Find("[data-testid=share-save]").ClickAsync(new());

        Assert.Empty(Staff.Shares);
        Assert.NotNull(Dialogs.Find("[data-testid=share-error]"));
    }

    [Fact]
    public async Task Share_dialog_shows_policy_block_message()
    {
        Culture.Current = new CultureInfo("en");
        await OpenAsync();
        await FillRowAsync();
        Staff.WriteFailure = new ApiProblemException(409, "BlockedByPolicy", "Teachers cannot be assigned here.", null);

        await Dialogs.Find("[data-testid=share-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() =>
            Assert.Contains("Teachers cannot be assigned here.", Dialogs.Find("[data-testid=share-error]").TextContent));
    }
}
