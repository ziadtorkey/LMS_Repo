using System.Globalization;
using Bunit;
using Lms.Client.Pages.Students;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Students;

public sealed class StudentProfilePageTests : StudentsTestContext
{
    private StudentDetailDto Student => Students.All[0];

    private async Task<IRenderedComponent<StudentProfilePage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        return Render<StudentProfilePage>(p => p.Add(x => x.Id, Student.Summary.Id));
    }

    [Fact]
    public async Task Profile_shows_account_state_and_reset_password_shows_credentials_once()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.View, Permissions.Students.Update);
        Assert.Contains("No portal account yet", cut.WaitForElement("[data-testid=profile-account]").TextContent);
        Assert.Contains("Ahmed Saeed", cut.Find("[data-testid=profile-name]").TextContent);

        // The click handlers await dialogs, so the clicks are not awaited.
        _ = cut.Find("[data-testid=action-reset]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        var password = await Dialogs.WaitForElementAsync("input[data-testid=credentials-password]");
        Assert.Equal("Reset#Pass-7342", password.GetAttribute("value"));
        Assert.Equal([Student.Summary.Id], Students.PasswordResets);
        await Dialogs.Find("[data-testid=credentials-done]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() =>
            Assert.Contains("Must change the default password", cut.Find("[data-testid=profile-account]").TextContent));
    }

    [Fact]
    public async Task Nationality_is_shown_as_the_localized_country_name()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.View);
        var english = Nationalities.Find("EG")!.NameEn;
        Assert.Contains(english, cut.WaitForElement("[data-testid=profile-nationality]").TextContent);
        Assert.DoesNotContain("EG", cut.Find("[data-testid=profile-nationality]").TextContent.Replace(english, ""));

        Culture.Current = new CultureInfo("ar");
        var arabic = Render<StudentProfilePage>(p => p.Add(x => x.Id, Student.Summary.Id));
        Assert.Contains(Nationalities.Find("EG")!.NameAr, arabic.WaitForElement("[data-testid=profile-nationality]").TextContent);
    }

    [Fact]
    public async Task Header_links_to_current_class_or_shows_unplaced()
    {
        var cut = await RenderPageAsync(Permissions.Students.View);
        var link = cut.WaitForElement("[data-testid=profile-class]");
        Assert.EndsWith($"classes/{Student.Summary.CurrentClassId}", link.GetAttribute("href"));

        var unplaced = Students.All[1];
        var other = Render<StudentProfilePage>(p => p.Add(x => x.Id, unplaced.Summary.Id));
        other.WaitForElement("[data-testid=profile-unplaced]");
    }

    [Fact]
    public async Task Edit_hides_code_in_auto_mode_and_sends_it_in_manual_mode()
    {
        var cut = await RenderPageAsync(Permissions.Students.View, Permissions.Students.Update);
        _ = cut.WaitForElement("[data-testid=action-edit]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=dialog-save]");
        Assert.Empty(Dialogs.FindAll("input[data-testid=student-code]"));

        // Manual mode: the code is editable and sent.
        await Dialogs.Find("[data-testid=dialog-cancel]").ClickAsync(new());
        Settings.Values[SettingKeys.StudentsCodeMode.Name] = "\"Manual\"";
        _ = cut.Find("[data-testid=action-edit]").ClickAsync(new());
        var code = await Dialogs.WaitForElementAsync("input[data-testid=student-code]");
        code.Change("S-77");
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await Dialogs.WaitForAssertionAsync(() => Assert.Single(Students.Updates));
        Assert.Equal("S-77", Students.Updates[0].Request.StudentCode);
    }

    [Fact]
    public async Task Edit_in_auto_mode_sends_no_code()
    {
        var cut = await RenderPageAsync(Permissions.Students.View, Permissions.Students.Update);
        _ = cut.WaitForElement("[data-testid=action-edit]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=dialog-save]");
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await Dialogs.WaitForAssertionAsync(() => Assert.Single(Students.Updates));
        Assert.Null(Students.Updates[0].Request.StudentCode);
        Assert.Equal(Student.Summary.HomeOrgUnitId, Students.Updates[0].Request.HomeOrgUnitId);
    }

    [Fact]
    public async Task Deactivate_asks_for_a_reason_and_records_it_in_history()
    {
        var cut = await RenderPageAsync(Permissions.Students.View, Permissions.Students.Update);
        _ = cut.WaitForElement("[data-testid=action-status]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("textarea[data-testid=status-reason]")).Input("Moved abroad");
        await Dialogs.Find("[data-testid=status-confirm]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(Students.StatusChanges));
        Assert.Equal((PersonStatuses.Deactivated, "Moved abroad"), (Students.StatusChanges[0].Request.Status, Students.StatusChanges[0].Request.Reason));
    }

    [Fact]
    public async Task Delete_asks_for_confirmation_and_goes_back_to_the_list()
    {
        var id = Student.Summary.Id;
        var cut = await RenderPageAsync(Permissions.Students.View, Permissions.Students.Delete);
        Assert.Empty(cut.FindAll("[data-testid=action-edit]"));
        _ = cut.WaitForElement("[data-testid=action-delete]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal([id], Students.Deleted));
        Assert.EndsWith("/students", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
    }

    [Fact]
    public async Task Actions_hidden_without_Students_Update_or_Delete()
    {
        var cut = await RenderPageAsync(Permissions.Students.View);

        cut.WaitForElement("[data-testid=profile-name]");
        foreach (var action in new[] { "action-edit", "action-status", "action-reset", "action-delete" })
            Assert.Empty(cut.FindAll($"[data-testid={action}]"));
    }

    [Fact]
    public async Task Profile_tabs_render_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Students.View, Permissions.Students.Update);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true)
            .AddChildContent<StudentProfilePage>(c => c.Add(x => x.Id, Student.Summary.Id)));

        Assert.Contains("أحمد سعيد", rtl.WaitForElement("[data-testid=profile-name]").TextContent);
        var tabs = rtl.Find("[data-testid=profile-tabs]");
        Assert.Contains("أولياء الأمور", tabs.TextContent);
        Assert.Contains("الميزات", tabs.TextContent);
        // The personal details are the left/start card of the profile grid, not a tab.
        Assert.Contains("الملف الشخصي", rtl.Find("[data-testid=tab-profile]").TextContent);
        // Codes and contact values stay LTR whatever the page direction.
        Assert.Equal("ltr", rtl.Find("[data-testid=profile-code]").GetAttribute("dir"));
    }
}
