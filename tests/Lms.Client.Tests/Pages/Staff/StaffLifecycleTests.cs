using System.Globalization;
using Bunit;
using Lms.Client.Pages.Staff;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;

namespace Lms.Client.Tests.Pages.Staff;

public sealed class StaffLifecycleTests : StaffTestContext
{
    private static readonly string[] All =
        [Permissions.Staff.View, Permissions.Staff.Update, Permissions.Staff.Delete, Permissions.Assignments.Create, Permissions.Assignments.Delete];

    private async Task<IRenderedComponent<StaffProfilePage>> RenderAsync(Guid id, params string[] permissions)
    {
        await GrantAsync(permissions.Length == 0 ? All : permissions);
        RenderProviders();
        var cut = Render<StaffProfilePage>(p => p.Add(x => x.Id, id));
        cut.WaitForElement("[data-testid=profile-name]");
        return cut;
    }

    private StaffDetailDto Sara => Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed");

    [Fact]
    public async Task Deactivate_requires_reason_and_sends_status_request()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderAsync(Sara.Summary.Id);

        _ = cut.Find("[data-testid=action-status]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=status-confirm]");

        await Dialogs.Find("[data-testid=status-confirm]").ClickAsync(new());
        Assert.Empty(Staff.StatusChanges);

        Dialogs.Find("textarea[data-testid=status-reason]").Input("Left the school");
        await Dialogs.Find("[data-testid=status-confirm]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Single(Staff.StatusChanges));
        var (id, request) = Staff.StatusChanges[0];
        Assert.Equal(Sara.Summary.Id, id);
        Assert.Equal((PersonStatuses.Deactivated, "Left the school"), (request.Status, request.Reason));
        cut.WaitForAssertion(() => Assert.Contains("Reactivate", cut.Find("[data-testid=action-status]").TextContent));
    }

    [Fact]
    public async Task Own_profile_hides_status_and_delete()
    {
        Api.Me = Api.Me with { UserId = Sara.Summary.UserId };
        var cut = await RenderAsync(Sara.Summary.Id);

        Assert.NotNull(cut.Find("[data-testid=action-edit]"));
        Assert.Empty(cut.FindAll("[data-testid=action-status]"));
        Assert.Empty(cut.FindAll("[data-testid=action-delete]"));
    }

    [Fact]
    public async Task Actions_are_hidden_without_permissions()
    {
        var cut = await RenderAsync(Sara.Summary.Id, Permissions.Staff.View);

        Assert.Empty(cut.FindAll("[data-testid=action-edit]"));
        Assert.Empty(cut.FindAll("[data-testid=action-status]"));
        Assert.Empty(cut.FindAll("[data-testid=action-share]"));
        Assert.Empty(cut.FindAll("[data-testid=action-delete]"));
    }

    [Fact]
    public async Task Resend_shown_only_when_not_activated()
    {
        var pending = Staff.Add("جديد", "Newcomer", "new@x.test", PersonStatuses.Active, [], activated: false);
        var cut = await RenderAsync(pending.Summary.Id);

        await cut.Find("[data-testid=action-resend]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(pending.Summary.Id, Assert.Single(Staff.Invitations)));

    }

    [Fact]
    public async Task Resend_is_not_offered_for_an_activated_person()
    {
        var cut = await RenderAsync(Sara.Summary.Id);
        Assert.Empty(cut.FindAll("[data-testid=action-resend]"));
    }

    [Fact]
    public async Task Delete_confirms_then_deletes()
    {
        var sara = Sara.Summary;
        var cut = await RenderAsync(sara.Id);

        _ = cut.Find("[data-testid=action-delete]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=confirm-yes]");
        await Dialogs.Find("[data-testid=confirm-yes]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains(sara.Id, Staff.Deleted));
    }

    [Fact]
    public async Task Edit_dialog_sends_the_changed_fields()
    {
        var sara = Sara.Summary;
        var cut = await RenderAsync(sara.Id);

        _ = cut.Find("[data-testid=action-edit]").ClickAsync(new());
        Dialogs.WaitForElement("input[data-testid=staff-name-en]");
        Dialogs.Find("input[data-testid=staff-name-en]").Change("Sara A.");
        Assert.NotNull(Dialogs.Find("input[data-testid=staff-email][readonly]"));
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Single(Staff.Updates));
        Assert.Equal("Sara A.", Staff.Updates[0].Request.NameEn);
        Assert.Equal(sara.HomeOrgUnitId, Staff.Updates[0].Request.HomeOrgUnitId);
    }

    [Fact]
    public async Task Removing_a_share_calls_the_api()
    {
        var khaled = Staff.All.First(s => s.Summary.NameEn == "Khaled Ali");
        var share = khaled.Summary.Roles.First(r => r.IsShare);
        var cut = await RenderAsync(khaled.Summary.Id);

        await cut.Find($"[data-testid=remove-share-{share.AssignmentId}]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Contains((khaled.Summary.Id, share.AssignmentId), Staff.RemovedShares));
    }

    [Fact]
    public async Task Expiring_staff_show_warning_in_list()
    {
        var soon = Staff.Add("قريب", "Soon", "soon@x.test", PersonStatuses.Active, [], DateOnly.FromDateTime(DateTime.Today).AddDays(5));
        var later = Staff.Add("بعيد", "Later", "later@x.test", PersonStatuses.Active, [], DateOnly.FromDateTime(DateTime.Today).AddDays(60));
        await GrantAsync(Permissions.Staff.View);
        RenderProviders();
        var cut = Render<StaffListPage>();

        cut.WaitForElement($"[data-testid=expiry-warning-{soon.Summary.Id}]");
        Assert.Empty(cut.FindAll($"[data-testid=expiry-warning-{later.Summary.Id}]"));
    }

    [Theory]
    [InlineData("ar", "إيقاف")]
    [InlineData("en", "Deactivate")]
    public async Task Dialogs_render_in_the_active_language(string culture, string deactivate)
    {
        Culture.Current = new CultureInfo(culture);
        var cut = await RenderAsync(Sara.Summary.Id);

        _ = cut.Find("[data-testid=action-status]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=status-confirm]");

        Assert.Contains(deactivate, Dialogs.Find("[data-testid=status-confirm]").TextContent);
        Assert.Equal(culture == "ar", Culture.IsRightToLeft);
    }
}
