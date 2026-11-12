using System.Globalization;
using Bunit;
using Lms.Client.Pages.Enrollment;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.Enrollment;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Enrollment;

public sealed class GroupsTabTests : StudentsTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly Guid ClassCourse = FakeEnrollmentApi.ClassCourseId2;
    private static readonly Guid Ahmed = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Laila = Guid.Parse("00000000-0000-0000-0000-0000000000c2");
    private static readonly Guid Omar = Guid.Parse("00000000-0000-0000-0000-0000000000c3");
    private static readonly Guid Sara = Guid.Parse("00000000-0000-0000-0000-0000000000c4");

    private readonly FakeEnrollmentApi _enrollment = new();
    private readonly FakeGroupsApi _groups = new();
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public GroupsTabTests()
    {
        Services.AddSingleton<IEnrollmentApi>(_enrollment);
        Services.AddSingleton<IGroupsApi>(_groups);
    }

    private StudentGroupDto GroupA => _groups.ListAsync(ClassCourse).Result.First();

    private IRenderedComponent<GroupsTab> RenderTab(bool manage = true)
    {
        RenderProviders();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<GroupsTab>(p => p.Add(t => t.ClassCourseId, ClassCourse).Add(t => t.CanManage, manage));
    }

    private async Task OpenMembersAsync(IRenderedComponent<GroupsTab> cut, StudentGroupDto group)
    {
        _ = cut.WaitForElement($"[data-testid=group-members-{group.Id}]").ClickAsync(new());
        await Dialogs.WaitForElementAsync("[data-testid=members-list]");
    }

    [Fact]
    public async Task Tab_lists_groups_with_match_and_member_counts()
    {
        Culture.Current = new CultureInfo("en");
        var female = _groups.Seed(ClassCourse, "بنات", "Girls", CourseRestrictionTypes.Gender, "Female");
        var cut = RenderTab();

        cut.WaitForElement("[data-testid=groups-table]");
        Assert.Equal("1", cut.Find($"[data-testid=group-count-{GroupA.Id}]").TextContent.Trim());
        Assert.Equal("—", cut.Find($"[data-testid=group-match-{GroupA.Id}]").TextContent.Trim());
        Assert.Equal("Gender: Female", cut.Find($"[data-testid=group-match-{female.Id}]").TextContent.Trim());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Create_group_sends_names_and_the_optional_match()
    {
        Culture.Current = new CultureInfo("en");
        var cut = RenderTab();
        _ = cut.WaitForElement("[data-testid=group-new]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("input[data-testid=group-name-ar]")).Change("مجموعة ب");
        Dialogs.Find("input[data-testid=group-name-en]").Change("Group B");
        var type = Dialogs.FindComponents<MudSelect<string>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "group-match-type");
        await Dialogs.InvokeAsync(() => type.Instance.ValueChanged.InvokeAsync(CourseRestrictionTypes.Gender));
        var value = Dialogs.FindComponents<MudSelect<string>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == "group-match-value");
        await Dialogs.InvokeAsync(() => value.Instance.ValueChanged.InvokeAsync("Female"));

        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Single(_groups.Created), Wait);
        Assert.Equal(ClassCourse, _groups.Created[0].ClassCourseId);
        Assert.Equal(new SaveGroupRequest("مجموعة ب", "Group B", CourseRestrictionTypes.Gender, "Female"), _groups.Created[0].Request);
        await cut.WaitForAssertionAsync(() => Assert.Contains("Group B", cut.Markup), Wait);
    }

    [Fact]
    public async Task Rename_and_delete_a_group()
    {
        var group = GroupA;
        var cut = RenderTab();
        _ = cut.WaitForElement($"[data-testid=group-edit-{group.Id}]").ClickAsync(new());
        (await Dialogs.WaitForElementAsync("input[data-testid=group-name-en]")).Change("Group Alpha");
        await Dialogs.Find("[data-testid=dialog-save]").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => Assert.Single(_groups.Updated), Wait);
        Assert.Equal((group.Id, "Group Alpha"), (_groups.Updated[0].Id, _groups.Updated[0].Request.NameEn));

        _ = cut.WaitForElement($"[data-testid=group-delete-{group.Id}]").ClickAsync(new());
        await Dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());

        await cut.WaitForAssertionAsync(() => Assert.Equal([group.Id], _groups.Deleted), Wait);
        await cut.WaitForElementAsync("[data-testid=groups-empty]", Wait);
    }

    [Fact]
    public async Task Members_picker_excludes_students_in_other_groups()
    {
        var other = _groups.Seed(ClassCourse, "مجموعة ب", "Group B", null, null, _groups.Directory[2]); // Omar is in Group B
        var cut = RenderTab();
        await OpenMembersAsync(cut, GroupA); // Ahmed is a member of Group A

        // Left: only the ungrouped (Laila, Sara). Ahmed (member) and Omar (other group) are not offered.
        Assert.NotEmpty(Dialogs.FindAll($"input[data-testid=ungrouped-{Laila}]"));
        Assert.NotEmpty(Dialogs.FindAll($"input[data-testid=ungrouped-{Sara}]"));
        Assert.Empty(Dialogs.FindAll($"input[data-testid=ungrouped-{Omar}]"));
        Assert.Empty(Dialogs.FindAll($"input[data-testid=ungrouped-{Ahmed}]"));
        Assert.NotEmpty(Dialogs.FindAll($"input[data-testid=member-{Ahmed}]"));
        Assert.NotNull(other);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Add_selected_and_remove_selected_move_students_between_the_lists()
    {
        var cut = RenderTab();
        await OpenMembersAsync(cut, GroupA);

        await Dialogs.Find($"input[data-testid=ungrouped-{Laila}]").ChangeAsync(new() { Value = true });
        await Dialogs.Find("[data-testid=members-add]").ClickAsync(new());

        await Dialogs.WaitForAssertionAsync(() => Assert.Single(_groups.Added), Wait);
        Assert.Equal([Laila], _groups.Added[0].StudentIds);
        await Dialogs.WaitForElementAsync($"input[data-testid=member-{Laila}]", Wait);
        Assert.Empty(Dialogs.FindAll($"input[data-testid=ungrouped-{Laila}]"));

        await Dialogs.Find($"input[data-testid=member-{Ahmed}]").ChangeAsync(new() { Value = true });
        await Dialogs.Find("[data-testid=members-remove]").ClickAsync(new());

        await Dialogs.WaitForAssertionAsync(() => Assert.Single(_groups.Removed), Wait);
        Assert.Equal([Ahmed], _groups.Removed[0].StudentIds);
        await Dialogs.WaitForElementAsync($"input[data-testid=ungrouped-{Ahmed}]", Wait);
    }

    [Fact]
    public async Task Add_suggested_moves_matching_students()
    {
        var matched = _groups.Seed(ClassCourse, "بنات", "Girls", CourseRestrictionTypes.Gender, "Female");
        var cut = RenderTab();
        await OpenMembersAsync(cut, matched);

        await Dialogs.Find("[data-testid=members-suggested]").ClickAsync(new());

        await Dialogs.WaitForAssertionAsync(() => Assert.Single(_groups.Added), Wait);
        Assert.Equal([Sara], _groups.Added[0].StudentIds); // the fake's suggestion
        await Dialogs.WaitForElementAsync($"input[data-testid=member-{Sara}]", Wait);
    }

    [Fact]
    public async Task Suggest_button_only_for_a_group_with_a_match()
    {
        var cut = RenderTab();
        await OpenMembersAsync(cut, GroupA); // Group A has no match

        Assert.Empty(Dialogs.FindAll("[data-testid=members-suggested]"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Conflict_shows_already_in_group()
    {
        Culture.Current = new CultureInfo("en");
        // Omar is put in Group B behind the dialog's back: adding him to Group A is a conflict.
        var other = _groups.Seed(ClassCourse, "مجموعة ب", "Group B");
        var cut = RenderTab();
        await OpenMembersAsync(cut, GroupA);
        await _groups.AddMembersAsync(other.Id, new GroupMembersRequest([Omar]));
        await Dialogs.Find($"input[data-testid=ungrouped-{Omar}]").ChangeAsync(new() { Value = true });

        await Dialogs.Find("[data-testid=members-add]").ClickAsync(new());

        Assert.Contains("The student is already in a group of this class course.", (await Dialogs.WaitForElementAsync("[data-testid=dialog-error]", Wait)).TextContent);
        await Dialogs.WaitForAssertionAsync(() => Assert.Empty(Dialogs.FindAll($"input[data-testid=ungrouped-{Omar}]")), Wait); // the lists reloaded
    }

    [Fact]
    public async Task Manage_actions_hidden_without_Groups_Manage()
    {
        var cut = RenderTab(manage: false);

        cut.WaitForElement("[data-testid=groups-table]");
        Assert.Empty(cut.FindAll("[data-testid=group-new]"));
        Assert.Empty(cut.FindAll($"[data-testid=group-members-{GroupA.Id}]"));
        Assert.Empty(cut.FindAll($"[data-testid=group-edit-{GroupA.Id}]"));
        Assert.Empty(cut.FindAll($"[data-testid=group-delete-{GroupA.Id}]"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Page_is_forbidden_without_Groups_View_and_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Classes.View);
        RenderProviders();
        var forbidden = Render<GroupsPage>(p => p.Add(x => x.ClassCourseId, ClassCourse));
        forbidden.WaitForAssertion(() => Assert.Contains("You do not have permission", forbidden.Markup));

        Culture.Current = new CultureInfo("ar");
        await GrantAsync(Permissions.Groups.View, Permissions.Groups.Manage);
        var rtl = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<GroupsPage>(c => c.Add(x => x.ClassCourseId, ClassCourse)));
        Assert.Contains("مجموعات الطلاب", rtl.WaitForElement("[data-testid=groups-title]").TextContent);
        Assert.Contains("المجموعة أ", rtl.WaitForElement("[data-testid=groups-table]").TextContent);
        Assert.Contains("مجموعة جديدة", rtl.Find("[data-testid=group-new]").TextContent);
    }
}
