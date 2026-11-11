using System.Globalization;
using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Pages.Courses;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Courses;

public sealed class CourseDialogTests : CoursesTestContext
{
    private async Task OpenNewAsync()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Update);
        RenderProviders();
        var page = Render<CourseListPage>();
        // The click handler awaits the dialog result, so do not await the click itself.
        _ = page.Find("[data-testid=course-new]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=dialog-save]");
    }

    private void Type(string id, string value) => Dialogs.Find($"input[data-testid={id}]").Change(value);

    private async Task FillValidAsync(string code = "PHY101")
    {
        Type("course-code", code);
        Type("course-name-ar", "الفيزياء");
        Type("course-name-en", "Physics");
        await Dialogs.InvokeAsync(() => SelectWithTestId(Dialogs, "course-owner").Instance.ValueChanged.InvokeAsync(FakeUnits.SchoolId));
    }

    [Fact]
    public async Task Owner_picker_offers_root_and_course_management_units_only()
    {
        await OpenNewAsync();

        var picker = Dialogs.FindComponents<UnitPicker>().First(u => u.Instance.TestId == "course-owner");
        picker.Find("div.mud-input-control").MouseDown();

        var offered = Popovers.FindComponents<MudSelectItem<Guid?>>().Select(i => i.Instance.Value).ToList();
        Assert.Contains((Guid?)FakeUnits.GroupId, offered);
        Assert.Contains((Guid?)FakeUnits.SchoolId, offered);
        Assert.DoesNotContain((Guid?)FakeUnits.GradeId, offered);
    }

    [Fact]
    public async Task Valid_form_posts_the_request_with_allocations_and_closes()
    {
        await OpenNewAsync();
        await FillValidAsync();
        var allocate = Dialogs.FindComponent<MudSelect<Guid>>();
        await Dialogs.InvokeAsync(() => allocate.Instance.SelectedValuesChanged.InvokeAsync(new[] { FakeUnits.GradeId }));

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.NotNull(Courses.LastSave));
        var sent = Courses.LastSave!;
        Assert.Equal("PHY101", sent.Code);
        Assert.Equal("Physics", sent.NameEn);
        Assert.Equal(FakeUnits.SchoolId, sent.OwnerOrgUnitId);
        Assert.Equal([FakeUnits.GradeId], sent.AllocateToOrgUnitIds);
        Dialogs.WaitForAssertion(() => Assert.Empty(Dialogs.FindAll("[data-testid=dialog-save]")));
    }

    [Fact]
    public async Task Required_fields_block_the_save()
    {
        await OpenNewAsync();

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Assert.Null(Courses.LastSave);
    }

    [Theory]
    [InlineData("bad code")]
    [InlineData("رمز")]
    public async Task Code_must_match_the_allowed_pattern(string code)
    {
        await OpenNewAsync();
        await FillValidAsync(code);

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Assert.Null(Courses.LastSave);
        Assert.Contains("Letters, digits", Dialogs.Markup);
    }

    [Fact]
    public async Task Code_taken_shows_field_message()
    {
        await OpenNewAsync();
        await FillValidAsync("math101");

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Contains("Another course already uses this code", Dialogs.Markup));
        Assert.NotEmpty(Dialogs.FindAll("[data-testid=dialog-save]"));
    }

    [Fact]
    public async Task Invalid_owner_unit_is_shown_as_a_form_error()
    {
        await OpenNewAsync();
        await FillValidAsync();
        Courses.WriteFailure = new ApiProblemException(422, "invalid_owner_unit", null, null);

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Contains("course-management level", Dialogs.Find("[data-testid=dialog-error]").TextContent));
    }

    [Fact]
    public async Task Editing_loads_the_course_and_sends_an_update()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View, Permissions.Courses.Update);
        RenderProviders();
        var page = Render<CourseListPage>();
        var math = Courses.All.First(c => c.Summary.Code == "MATH101").Summary.Id;
        _ = page.WaitForElement($"[data-testid=course-link-{math}]").ClickAsync(new());
        Dialogs.WaitForAssertion(() => Assert.Equal("MATH101", Dialogs.Find("input[data-testid=course-code]").GetAttribute("value")));

        Type("course-name-en", "Maths");
        Assert.Empty(Dialogs.FindAll("[data-testid=course-allocate]"));
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        Dialogs.WaitForAssertion(() => Assert.Equal(math, Courses.LastUpdatedId));
        Assert.Equal("Maths", Courses.LastSave!.NameEn);
        Assert.Null(Courses.LastSave.AllocateToOrgUnitIds);
    }

    [Fact]
    public async Task Code_field_is_left_to_right_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Courses.View, Permissions.Courses.Create);
        RenderProviders();
        var page = Render<CourseListPage>();
        _ = page.Find("[data-testid=course-new]").ClickAsync(new());
        Dialogs.WaitForElement("[data-testid=dialog-save]");

        Assert.Equal("ltr", Dialogs.Find("input[data-testid=course-code]").GetAttribute("dir"));
    }
}
