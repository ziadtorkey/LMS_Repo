using System.Globalization;
using Bunit;
using Lms.Client.Pages.Me;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Me;

public sealed class MyStudentProfilePageTests : StudentsTestContext
{
    private readonly FakeMyProfileApi _api = new();

    public MyStudentProfilePageTests() => Services.AddSingleton<IMyProfileApi>(_api);

    private async Task<IRenderedComponent<MyStudentProfilePage>> RenderPageAsync(params string[] permissions)
    {
        await GrantAsync(permissions);
        RenderProviders();
        return Render<MyStudentProfilePage>();
    }

    [Fact]
    public async Task Critical_fields_are_read_only_with_request_button()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);

        cut.WaitForElement("[data-testid=my-profile-title]");
        // Critical fields are plain text (no input), editable ones are inputs.
        foreach (var field in StudentFields.DefaultCritical)
        {
            Assert.NotEmpty(cut.FindAll($"[data-testid=readonly-{field}]"));
            Assert.Empty(cut.FindAll($"input[data-testid=field-{field}]"));
        }
        Assert.NotEmpty(cut.FindAll("input[data-testid=field-Mobile]"));
        Assert.NotEmpty(cut.FindAll("input[data-testid=field-NicknameEn]"));
        Assert.Contains("Ahmed Saeed", cut.Find("[data-testid=readonly-NameEn]").TextContent);
        Assert.False(cut.Find("[data-testid=request-change]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Editable_inputs_show_the_students_current_values()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);
        var current = (await _api.GetAsync()).Student;

        Assert.Equal("Ahmoudy", cut.WaitForElement("input[data-testid=field-NicknameEn]").GetAttribute("value"));
        Assert.Equal(current.NicknameAr, cut.Find("input[data-testid=field-NicknameAr]").GetAttribute("value"));
        Assert.Equal(current.Mobile, cut.Find("input[data-testid=field-Mobile]").GetAttribute("value"));
        Assert.DoesNotContain("_values", cut.Markup); // a literal parameter string must never reach the screen
    }

    [Fact]
    public async Task Request_dialog_inputs_show_the_current_values_of_every_requestable_field()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);
        var student = (await _api.GetAsync()).Student;
        _ = cut.WaitForElement("[data-testid=request-change]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("input[data-testid=field-NameEn]");

        Assert.Equal(student.Summary.NameAr, Dialogs.Find("input[data-testid=field-NameAr]").GetAttribute("value"));
        Assert.Equal(student.Summary.NameEn, Dialogs.Find("input[data-testid=field-NameEn]").GetAttribute("value"));
        Assert.Equal(student.NationalId, Dialogs.Find("input[data-testid=field-NationalId]").GetAttribute("value"));
        Assert.Equal(student.Summary.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Lms.Client.Components.People.StudentFieldText.Current(student, StudentFields.DateOfBirth));
        Assert.Contains("Male", Dialogs.Find("[data-testid=field-Gender]").TextContent + Dialogs.Find("[data-testid=field-Gender]").InnerHtml);
        Assert.DoesNotContain("_values", Dialogs.Markup);
    }

    [Fact]
    public async Task Save_sends_only_changed_editable_fields()
    {
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);
        cut.WaitForElement("input[data-testid=field-Mobile]").Change("0509998887");

        await cut.Find("[data-testid=my-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_api.Updates));
        var changes = _api.Updates[0].Changes;
        Assert.Equal("0509998887", Assert.Single(changes).Value);
        Assert.Equal(StudentFields.Mobile, changes.Keys.Single());
    }

    [Fact]
    public async Task Request_dialog_sends_only_changed_requestable_fields()
    {
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);
        _ = cut.WaitForElement("[data-testid=request-change]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("input[data-testid=field-NameEn]")).Change("Ahmed Said");
        Dialogs.Find("[data-testid=request-note]").Change("Spelling");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_api.Requests));
        var request = _api.Requests[0];
        Assert.Equal(StudentFields.NameEn, Assert.Single(request.Changes).Key);
        Assert.Equal("Ahmed Said", request.Changes[StudentFields.NameEn]);
        Assert.Equal("Spelling", request.Note);
    }

    [Fact]
    public async Task Request_dialog_without_changes_shows_a_message_and_sends_nothing()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);
        _ = cut.WaitForElement("[data-testid=request-change]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=dialog-save]");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Assert.Contains("Change at least one value", Dialogs.WaitForElement("[data-testid=dialog-error]").TextContent);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task Pending_request_blocks_new_request_with_message_and_can_be_cancelled()
    {
        Culture.Current = new CultureInfo("en");
        var pending = _api.Seed(ChangeRequestStatuses.Pending, new FieldChangeDto(StudentFields.NameEn, "Ahmed Saeed", "Ahmed Said"));
        _api.Seed(ChangeRequestStatuses.Rejected, new FieldChangeDto(StudentFields.Gender, "Male", "Female"));
        var cut = await RenderPageAsync(Permissions.Students.SelfEdit);

        cut.WaitForElement("[data-testid=pending-message]");
        Assert.True(cut.Find("[data-testid=request-change]").HasAttribute("disabled"));
        Assert.Contains("Pending", cut.Find($"[data-testid=request-status-{pending.Id}]").TextContent);
        Assert.Contains("Name does not match the record.", cut.Find("[data-testid=requests-table]").TextContent);

        await cut.Find($"[data-testid=request-cancel-{pending.Id}]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal([pending.Id], _api.Cancelled));
        await cut.WaitForAssertionAsync(() => Assert.Empty(cut.FindAll("[data-testid=pending-message]")));
        Assert.False(cut.Find("[data-testid=request-change]").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Page_is_forbidden_without_Students_SelfEdit()
    {
        Culture.Current = new CultureInfo("en");
        var cut = await RenderPageAsync(Permissions.Students.View);

        cut.WaitForAssertion(() => Assert.Contains("You do not have permission", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=my-profile-title]"));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Students.SelfEdit);
        RenderProviders();
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<MyStudentProfilePage>());

        Assert.Contains("ملفي الشخصي", rtl.WaitForElement("[data-testid=my-profile-title]").TextContent);
        Assert.Contains("الاسم بالعربية", rtl.Find("[data-testid=readonly-NameAr]").TextContent);
        Assert.Equal("ltr", rtl.Find("input[data-testid=field-Mobile]").GetAttribute("dir"));
        Assert.Equal("ltr", rtl.Find("[data-testid=my-code]").GetAttribute("dir"));
    }
}
