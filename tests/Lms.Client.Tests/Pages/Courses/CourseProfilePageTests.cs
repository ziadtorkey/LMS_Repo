using System.Globalization;
using Bunit;
using Lms.Client.Pages.Courses;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.People;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Courses;

public sealed class CourseProfilePageTests : CoursesTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private CourseDetailDto Math => Courses.All.First(c => c.Summary.Code == "MATH101");

    private async Task<IRenderedComponent<CourseProfilePage>> RenderPageAsync(Guid id, params string[] permissions)
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions);
        RenderProviders();
        var page = Render<CourseProfilePage>(p => p.Add(x => x.Id, id));
        page.WaitForElement("[data-testid=course-profile-name]");
        return page;
    }

    private async Task<IRenderedComponent<TTab>> RenderTabAsync<TTab>(Action<ComponentParameterCollectionBuilder<TTab>> parameters, params string[] permissions)
        where TTab : Microsoft.AspNetCore.Components.IComponent
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(permissions);
        RenderProviders();
        return Render(parameters);
    }

    private static string[] All => [Permissions.Courses.View, Permissions.Courses.Update, Permissions.Courses.Allocate,
        Permissions.Courses.ManageSupervisors, Permissions.Blueprints.View];

    // ---- page and tab visibility ----

    /// <summary>Headers of the tabs the user can see (MudBlazor keeps hidden tabs in the markup with a hidden class).</summary>
    private static List<string> VisibleTabs(IRenderedComponent<CourseProfilePage> page) =>
        page.FindAll("[data-testid=course-tabs] .mud-tab:not(.mud-tab-panel-hidden)").Select(t => t.TextContent.Trim()).ToList();

    [Fact]
    public async Task Profile_shows_details_and_all_tabs_for_a_full_editor()
    {
        var page = await RenderPageAsync(Math.Summary.Id, All);

        Assert.Contains("Mathematics", page.Find("[data-testid=course-profile-name]").TextContent);
        Assert.Contains("Main School", page.Find("[data-testid=course-profile-owner]").TextContent);
        Assert.NotNull(page.Find("[data-testid=course-edit]"));
        Assert.Equal(["Details", "Allocations", "Restrictions", "Supervisors", "Blueprint"], VisibleTabs(page));
    }

    [Fact]
    public async Task Tabs_hidden_by_permission_and_CanEdit()
    {
        var page = await RenderPageAsync(Math.Summary.Id, Permissions.Courses.View);

        Assert.Equal(["Details", "Restrictions", "Supervisors"], VisibleTabs(page));
        Assert.Empty(page.FindAll("[data-testid=course-edit]"));
    }

    [Fact]
    public async Task Inherited_course_has_no_edit_button_even_with_permission()
    {
        var arabic = Courses.All.First(c => c.Summary.Code == "ARB101");

        var page = await RenderPageAsync(arabic.Summary.Id, All);

        Assert.NotNull(page.Find("[data-testid=course-profile-inherited]"));
        Assert.Empty(page.FindAll("[data-testid=course-edit]"));
    }

    [Fact]
    public async Task Missing_course_shows_an_error()
    {
        Culture.Current = new CultureInfo("en");
        await GrantAsync(Permissions.Courses.View);
        RenderProviders();

        var page = Render<CourseProfilePage>(p => p.Add(x => x.Id, Guid.NewGuid()));

        page.WaitForElement("[data-testid=course-profile-error]");
    }

    // ---- allocations ----

    [Fact]
    public async Task Allocations_list_the_owner_and_lower_units()
    {
        var tab = await RenderTabAsync<AllocationsTab>(p => p.Add(x => x.Course, Math.Summary).Add(x => x.CanEdit, true), All);

        tab.WaitForElement("[data-testid=allocation-table]");
        Assert.Contains("Main School", tab.Markup);
        Assert.Contains("Grade 1", tab.Markup);
    }

    [Fact]
    public async Task Allocating_sends_the_unit_and_course()
    {
        var tab = await RenderTabAsync<AllocationsTab>(p => p.Add(x => x.Course, Math.Summary).Add(x => x.CanEdit, true), All);
        tab.WaitForElement("[data-testid=allocation-table]");

        await tab.InvokeAsync(() => SelectWithTestId(tab, "allocation-unit").Instance.ValueChanged.InvokeAsync(FakeUnits.GroupId));
        await tab.Find("[data-testid=allocation-add]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.NotNull(Courses.LastAllocate));
        Assert.Equal(FakeUnits.GroupId, Courses.LastAllocate!.OrgUnitId);
        Assert.Equal([Math.Summary.Id], Courses.LastAllocate.CourseIds);
    }

    [Fact]
    public async Task Allocation_in_use_shows_message()
    {
        var tab = await RenderTabAsync<AllocationsTab>(p => p.Add(x => x.Course, Math.Summary).Add(x => x.CanEdit, true), All);
        var row = Courses.AllocationsOf(Math.Summary.Id).First();
        Courses.AllocationFailure = new ApiProblemException(409, "allocation_in_use", null, null);

        await tab.WaitForElement($"[data-testid=allocation-remove-{row.Id}]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.False(string.IsNullOrWhiteSpace(tab.Find("[data-testid=allocation-error]").TextContent)));
        Assert.DoesNotContain("Something went wrong", tab.Find("[data-testid=allocation-error]").TextContent);
        Assert.Empty(Courses.RemovedAllocations);
    }

    [Fact]
    public async Task Removing_an_allocation_calls_the_api()
    {
        var tab = await RenderTabAsync<AllocationsTab>(p => p.Add(x => x.Course, Math.Summary).Add(x => x.CanEdit, true), All);
        var row = Courses.AllocationsOf(Math.Summary.Id).First();

        await tab.WaitForElement($"[data-testid=allocation-remove-{row.Id}]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.Contains(row.Id, Courses.RemovedAllocations));
    }

    [Fact]
    public async Task Allocations_are_read_only_without_edit_rights()
    {
        var tab = await RenderTabAsync<AllocationsTab>(p => p.Add(x => x.Course, Math.Summary).Add(x => x.CanEdit, false), All);

        tab.WaitForElement("[data-testid=allocation-table]");
        Assert.Empty(tab.FindAll("[data-testid=allocation-add]"));
        Assert.Empty(tab.FindAll("button[data-testid^=allocation-remove]"));
    }

    // ---- restrictions ----

    [Fact]
    public async Task Restriction_editor_sends_replace_all_and_shows_affected_warning()
    {
        Courses.AffectedEnrollments = 3;
        var tab = await RenderTabAsync<RestrictionsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, true), All);
        var gender = tab.FindComponents<MudSelect<string>>().First(s => (string?)s.Instance.UserAttributes["data-testid"] == "restriction-Gender");

        await tab.InvokeAsync(() => gender.Instance.SelectedValuesChanged.InvokeAsync(new[] { Genders.Male }));
        await tab.Find("[data-testid=restriction-save]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.NotNull(Courses.LastRestrictions));
        var sent = Courses.LastRestrictions!.Restrictions;
        Assert.Equal(2, sent.Count);
        Assert.Contains(sent, r => r.Type == CourseRestrictionTypes.Religion && r.Values.SequenceEqual([Religions.Muslim]));
        Assert.Contains(sent, r => r.Type == CourseRestrictionTypes.Gender && r.Values.SequenceEqual([Genders.Male]));
        Assert.Contains("3 current enrollments", tab.Find("[data-testid=restriction-affected]").TextContent);
    }

    [Fact]
    public async Task Clearing_every_restriction_sends_an_empty_list()
    {
        var tab = await RenderTabAsync<RestrictionsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, true), All);
        var religion = tab.FindComponents<MudSelect<string>>().First(s => (string?)s.Instance.UserAttributes["data-testid"] == "restriction-Religion");

        await tab.InvokeAsync(() => religion.Instance.SelectedValuesChanged.InvokeAsync(Array.Empty<string>()));
        await tab.Find("[data-testid=restriction-save]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.NotNull(Courses.LastRestrictions));
        Assert.Empty(Courses.LastRestrictions!.Restrictions);
        Assert.Empty(tab.FindAll("[data-testid=restriction-affected]"));
    }

    [Fact]
    public async Task Restrictions_are_shown_read_only_without_edit_rights()
    {
        var tab = await RenderTabAsync<RestrictionsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, false), All);

        Assert.Contains("Muslim", tab.Find("[data-testid=restriction-view-Religion]").TextContent);
        Assert.Empty(tab.FindAll("[data-testid=restriction-save]"));
    }

    // ---- supervisors ----

    [Fact]
    public async Task Supervisor_picker_lists_supervisors_only()
    {
        var tab = await RenderTabAsync<SupervisorsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, true), All);
        var supervisorRole = Access.Roles.First(r => r.BuiltInKey == BuiltInRoles.Supervisor).Id;
        var search = tab.FindComponent<MudAutocomplete<StaffSummaryDto>>();

        _ = await search.Instance.SearchFunc!("sa", CancellationToken.None)!;

        var query = Staff.LastQuery!;
        Assert.Equal(supervisorRole, query.RoleId);
        Assert.Equal(PersonStatuses.Active, query.Status);
    }

    [Fact]
    public async Task Adding_and_saving_supervisors_replaces_the_list()
    {
        var tab = await RenderTabAsync<SupervisorsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, true), All);
        var sara = Staff.All.First(s => s.Summary.NameEn == "Sara Ahmed").Summary;

        await tab.InvokeAsync(() => tab.FindComponent<MudAutocomplete<StaffSummaryDto>>().Instance.ValueChanged.InvokeAsync(sara));
        await tab.Find("[data-testid=supervisor-save]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.NotNull(Courses.LastSupervisors));
        Assert.Equal(2, Courses.LastSupervisors!.UserIds.Count);
        Assert.Contains(sara.UserId, Courses.LastSupervisors.UserIds);
        Assert.Contains(FakeCoursesApi.MeUserId, Courses.LastSupervisors.UserIds);
    }

    [Fact]
    public async Task Removing_a_supervisor_chip_drops_it_from_the_saved_list()
    {
        var tab = await RenderTabAsync<SupervisorsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, true), All);

        var chip = tab.FindComponent<MudChip<string>>();
        await tab.InvokeAsync(() => chip.Instance.OnClose.InvokeAsync(chip.Instance));
        await tab.Find("[data-testid=supervisor-save]").ClickAsync(new());

        tab.WaitForAssertion(() => Assert.NotNull(Courses.LastSupervisors));
        Assert.Empty(Courses.LastSupervisors!.UserIds);
    }

    [Fact]
    public async Task Supervisors_are_read_only_without_ManageSupervisors()
    {
        var tab = await RenderTabAsync<SupervisorsTab>(p => p.Add(x => x.Course, Math).Add(x => x.CanEdit, false), All);

        Assert.Contains("Supervisor", tab.Find("[data-testid=supervisor-list]").TextContent);
        Assert.Empty(tab.FindAll("[data-testid=supervisor-save]"));
        Assert.Empty(tab.FindAll("[data-testid=supervisor-search]"));
    }

    // ---- direction ----

    [Fact]
    public async Task Profile_renders_rtl_in_arabic()
    {
        Culture.Current = new CultureInfo("ar");
        var id = Math.Summary.Id;

        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body,
            (Microsoft.AspNetCore.Components.RenderFragment)(b =>
            {
                b.OpenComponent<CourseProfilePage>(0);
                b.AddAttribute(1, nameof(CourseProfilePage.Id), id);
                b.CloseComponent();
            })));
        await layout.InvokeAsync(() => GrantAsync(All));

        layout.WaitForElement("[data-testid=course-profile-name]", Wait);
        Assert.Equal("rtl", layout.Find("[data-testid=app-root]").GetAttribute("dir"));
        Assert.Contains("الرياضيات", layout.Find("[data-testid=course-profile-name]").TextContent);
        Assert.Contains("MATH101", layout.Find("[data-testid=course-profile-name]").ParentElement!.TextContent);
    }
}
