using System.Globalization;
using Bunit;
using Lms.Client.Pages.Access;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Access;

public sealed class AssignmentsPageTests : ClientTestContext
{
    private readonly FakeAccessAdminApi _access = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public AssignmentsPageTests()
    {
        Services.AddSingleton<IAccessAdminApi>(_access);
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
    }

    private async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private IRenderedComponent<AssignmentsPage> RenderPage()
    {
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<AssignmentsPage>();
    }

    private Guid TeacherRole => _access.Roles.First(r => r.BuiltInKey == BuiltInRoles.Teacher).Id;

    [Fact]
    public async Task Table_requests_server_pages()
    {
        await GrantAsync(Permissions.Assignments.View);
        _access.Seed("a@x.test", TeacherRole, FakeUnits.GradeId);
        var cut = RenderPage();

        cut.WaitForAssertion(() => Assert.NotNull(_access.LastQuery));
        Assert.Equal((FakeUnits.GroupId, false, 1, 50), _access.LastQuery!.Value);
        Assert.DoesNotContain("a@x.test", cut.Markup);

        await cut.Find("[data-testid=include-below]").ChangeAsync(new() { Value = true });

        cut.WaitForAssertion(() => Assert.True(_access.LastQuery!.Value.IncludeDescendants));
        cut.WaitForAssertion(() => Assert.Contains("a@x.test", cut.Markup));
    }

    [Fact]
    public async Task Status_chip_reflects_dates()
    {
        await GrantAsync(Permissions.Assignments.View);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var valid = _access.Seed("v@x.test", TeacherRole, FakeUnits.GroupId, today.AddDays(-5), today.AddDays(5));
        var future = _access.Seed("f@x.test", TeacherRole, FakeUnits.GroupId, today.AddDays(3), null);
        var expired = _access.Seed("e@x.test", TeacherRole, FakeUnits.GroupId, null, today.AddDays(-1));
        var cut = RenderPage();

        cut.WaitForElement($"[data-testid=status-{valid.Id}]");
        Assert.Equal("Valid", cut.Find($"[data-testid=status-{valid.Id}]").GetAttribute("data-status"));
        Assert.Equal("Future", cut.Find($"[data-testid=status-{future.Id}]").GetAttribute("data-status"));
        Assert.Equal("Expired", cut.Find($"[data-testid=status-{expired.Id}]").GetAttribute("data-status"));
    }

    [Fact]
    public async Task Actions_hidden_without_permissions()
    {
        await GrantAsync(Permissions.Assignments.View);
        _access.Seed("a@x.test", TeacherRole, FakeUnits.GroupId);
        var cut = RenderPage();

        cut.WaitForElement("[data-testid^=status-]");
        Assert.Empty(cut.FindAll("[data-testid=assign-new]"));
        Assert.Empty(cut.FindAll("[data-testid^=assignment-edit-]"));
        Assert.Empty(cut.FindAll("[data-testid^=assignment-delete-]"));
    }

    [Fact]
    public async Task Assign_posts_request_and_refreshes_table()
    {
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Create);
        var cut = RenderPage();

        await cut.WaitForElement("[data-testid=assign-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=assign-email]").Change("new@x.test");
        _dialogs.Find("[data-testid=assign-name-en]").Change("New Person");
        await _dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotNull(_access.LastAssignment));
        Assert.Equal("new@x.test", _access.LastAssignment!.Email);
        Assert.Equal("New Person", _access.LastAssignment.DisplayNameEn);
        Assert.Equal(FakeUnits.GroupId, _access.LastAssignment.OrgUnitId);
        cut.WaitForAssertion(() => Assert.Contains("new@x.test", cut.Markup));
    }

    [Fact]
    public async Task Assign_dialog_rejects_valid_to_before_valid_from()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Create);
        var cut = RenderPage();
        await cut.WaitForElement("[data-testid=assign-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=assign-email]").Change("new@x.test");

        var pickers = _dialogs.FindComponents<MudDatePicker>();
        await _dialogs.InvokeAsync(() => pickers[0].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 10)));
        await _dialogs.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 1)));
        await _dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        Assert.Contains("يجب ألا يسبق تاريخ الانتهاء تاريخ البدء", _dialogs.WaitForElement("[data-testid=assign-error]").TextContent);
        Assert.Null(_access.LastAssignment);
    }

    [Theory]
    [InlineData("permission_escalation", "لا يمكنك منح صلاحيات لا تملكها")]
    [InlineData("own_assignment", "لا يمكنك تعديل إسنادك الخاص")]
    public async Task Guard_errors_are_shown_from_problem_code(string code, string expected)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Create);
        var cut = RenderPage();
        await cut.WaitForElement("[data-testid=assign-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=assign-email]").Change("new@x.test");
        _access.NextFailure = new ApiProblemException(code == "own_assignment" ? 409 : 403, code, null, null);

        await _dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        Assert.Contains(expected, _dialogs.WaitForElement("[data-testid=assign-error]").TextContent);
    }


    [Fact]
    public void Unit_picker_order_is_depth_first_by_current_culture_name()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Lms.Shared.Organizations.OrgUnitNodeDto Unit(string en, Guid id, Guid? parent, int depth) =>
            new(id, parent, Guid.NewGuid(), en, en, en, depth, "Active", false, null, false, false, []);
        var root = Guid.NewGuid(); var b = Guid.NewGuid(); var a = Guid.NewGuid();
        var units = new[]
        {
            Unit("Grade B1", Guid.NewGuid(), b, 2), Unit("School A", a, root, 1), Unit("Root", root, null, 0),
            Unit("Grade A1", Guid.NewGuid(), a, 2), Unit("School B", b, root, 1),
        };

        var ordered = UnitTree.Order(units).Select(u => u.NameEn);

        Assert.Equal(["Root", "School A", "Grade A1", "School B", "Grade B1"], ordered);
    }

    public static TheoryData<Exception> RedirectingFailures => new()
    {
        { new ApiContextException("invalid_context") },
        { new ApiForbiddenException() },
    };

    [Theory]
    [MemberData(nameof(RedirectingFailures))]
    public async Task Assign_with_stale_context_or_forbidden_redirects_instead_of_crashing(Exception failure)
    {
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Create);
        var cut = RenderPage();
        await cut.WaitForElement("[data-testid=assign-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=assign-email]").Change("new@x.test");
        _access.NextFailure = failure;

        await _dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith(failure is ApiContextException ? "/choose-context" : "/forbidden", navigation.Uri);
    }

    [Fact]
    public async Task Assign_offline_shows_the_generic_error()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Create);
        var cut = RenderPage();
        await cut.WaitForElement("[data-testid=assign-new]").ClickAsync(new());
        _dialogs.WaitForElement("[data-testid=assign-email]").Change("new@x.test");
        _access.NextFailure = new HttpRequestException("offline");

        await _dialogs.Find("[data-testid=assign-save]").ClickAsync(new());

        Assert.Contains("Could not load data", _dialogs.WaitForElement("[data-testid=assign-error]").TextContent);
    }

    [Fact]
    public async Task Remove_offline_shows_a_snackbar_instead_of_crashing()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync(Permissions.Assignments.View, Permissions.Assignments.Delete);
        var row = _access.Seed("a@x.test", TeacherRole, FakeUnits.GroupId);
        var cut = RenderPage();
        _access.NextFailure = new HttpRequestException("offline");

        var click = cut.WaitForElement($"[data-testid=assignment-delete-{row.Id}]").ClickAsync(new());
        await _dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());
        await click;

        cut.WaitForAssertion(() => Assert.Contains("Could not load data", _snackbars.Markup));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.Assignments.View);
        _access.Seed("a@x.test", TeacherRole, FakeUnits.GroupId);
        Render<MudPopoverProvider>();
        var cut = Render<MudRTLProvider>(p => p.Add(r => r.RightToLeft, true).AddChildContent<AssignmentsPage>());

        cut.WaitForElement("[data-testid^=status-]");
        Assert.Contains("تعيين الأدوار", cut.Markup);
        Assert.Contains("ساري", cut.Markup);
        Assert.Contains("dir=\"ltr\"", cut.Markup);
    }
}
