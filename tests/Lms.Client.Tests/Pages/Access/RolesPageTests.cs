using System.Globalization;
using System.Xml.Linq;
using Bunit;
using Lms.Client.Pages.Access;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Access;

public sealed class RolesPageTests : ClientTestContext
{
    private readonly FakeAccessAdminApi _roles = new();
    private IRenderedComponent<MudDialogProvider> _dialogs = default!;
    private IRenderedComponent<MudSnackbarProvider> _snackbars = default!;

    public RolesPageTests() => Services.AddSingleton<IAccessAdminApi>(_roles);

    private async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private IRenderedComponent<RolesPage> RenderPage()
    {
        Render<MudPopoverProvider>();
        _dialogs = Render<MudDialogProvider>();
        _snackbars = Render<MudSnackbarProvider>();
        return Render<RolesPage>();
    }

    private static Dictionary<string, string> Load(string lang)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        var path = Path.Combine(dir!, "src", "Lms.Client", "Resources", $"SharedResource.{lang}.resx");
        return XDocument.Load(path).Descendants("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_non_platform_permission_has_ar_and_en_label(string lang)
    {
        var resources = Load(lang);
        var missing = PermissionGroups.Assignable
            .SelectMany(g => g.Permissions.Select(PermissionMatrix.PermissionLabelKey).Append($"PermModule_{g.Module}"))
            .Where(key => !resources.ContainsKey(key))
            .ToList();

        Assert.Empty(missing);
        Assert.DoesNotContain(Permissions.Clients.View, PermissionGroups.Assignable.SelectMany(g => g.Permissions));
        Assert.Equal(Permissions.All.Count - Permissions.PlatformOnly.Count, PermissionGroups.Assignable.Sum(g => g.Permissions.Count));
    }

    [Fact]
    public async Task Built_in_role_base_kind_and_delete_are_locked()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update, Permissions.Roles.Delete);
        var cut = RenderPage();
        var builtIn = _roles.Roles.First(r => r.IsBuiltIn);

        cut.WaitForElement($"[data-testid=role-builtin-{builtIn.Id}]");
        Assert.True(cut.Find($"[data-testid=role-delete-{builtIn.Id}]").HasAttribute("disabled"));

        await cut.Find($"[data-testid=role-edit-{builtIn.Id}]").ClickAsync(new());
        cut.WaitForElement("[data-testid=role-basekind-fixed]");
        Assert.Empty(cut.FindAll("[data-testid=role-basekind]"));
    }

    [Fact]
    public async Task Base_kind_can_be_chosen_only_when_creating()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Create);
        var cut = RenderPage();

        await cut.WaitForElement("[data-testid=role-new]").ClickAsync(new());

        cut.WaitForElement("[data-testid=role-basekind]");
        Assert.Empty(cut.FindAll("[data-testid=role-basekind-fixed]"));
    }

    [Fact]
    public async Task Create_and_delete_buttons_are_hidden_without_permission()
    {
        await GrantAsync(Permissions.Roles.View);
        var cut = RenderPage();

        cut.WaitForElement("table");
        Assert.Empty(cut.FindAll("[data-testid=role-new]"));
        Assert.Empty(cut.FindAll("[data-testid^=role-delete-]"));
        Assert.Empty(cut.FindAll("[data-testid^=role-edit-]"));
    }

    [Fact]
    public async Task Select_all_in_module_toggles_group()
    {
        var group = PermissionGroups.Assignable.First(g => g.Module == "Roles");
        var selected = new HashSet<string>();
        var cut = Render<PermissionMatrix>(p => p
            .Add(m => m.Groups, PermissionGroups.Assignable)
            .Add(m => m.Selected, selected));

        await cut.Find("[data-testid=select-module-Roles]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });
        Assert.True(group.Permissions.All(selected.Contains));
        Assert.DoesNotContain(Permissions.Settings.View, selected);

        await cut.Find("[data-testid=select-module-Roles]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = false });
        Assert.Empty(selected);
    }

    [Fact]
    public async Task Escalation_error_is_shown_from_problem_code()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update);
        var cut = RenderPage();
        var role = _roles.Roles.First();
        _roles.NextFailure = new ApiProblemException(403, "permission_escalation", null, null);

        await cut.WaitForElement($"[data-testid=role-edit-{role.Id}]").ClickAsync(new());
        await cut.WaitForElement("[data-testid=role-save]").ClickAsync(new());

        Assert.Contains("لا يمكنك منح صلاحيات لا تملكها", cut.WaitForElement("[data-testid=role-error]").TextContent);
    }

    [Fact]
    public async Task Saving_a_new_role_posts_the_selected_permissions()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Create);
        var cut = RenderPage();

        await cut.WaitForElement("[data-testid=role-new]").ClickAsync(new());
        cut.Find("[data-testid=name-ar]").Change("منسق");
        cut.Find("[data-testid=name-en]").Change("Coordinator");
        await cut.Find("[data-testid=\"perm-Audit.View\"]").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });
        await cut.Find("[data-testid=role-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotNull(_roles.LastCreate));
        Assert.Equal(["Audit.View"], _roles.LastCreate!.Permissions);
        Assert.Equal(RoleBaseKinds.Staff, _roles.LastCreate.BaseKind);
    }


    public static TheoryData<Exception> RedirectingFailures => new()
    {
        { new ApiContextException("invalid_context") },
        { new ApiForbiddenException() },
    };

    [Theory]
    [MemberData(nameof(RedirectingFailures))]
    public async Task Save_with_stale_context_or_forbidden_redirects_instead_of_crashing(Exception failure)
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update);
        var cut = RenderPage();
        _roles.NextFailure = failure;

        await cut.WaitForElement($"[data-testid=role-edit-{_roles.Roles.First().Id}]").ClickAsync(new());
        await cut.WaitForElement("[data-testid=role-save]").ClickAsync(new());

        var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        Assert.EndsWith(failure is ApiContextException ? "/choose-context" : "/forbidden", navigation.Uri);
    }

    [Fact]
    public async Task Save_offline_shows_the_generic_error()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update);
        var cut = RenderPage();
        _roles.NextFailure = new HttpRequestException("offline");

        await cut.WaitForElement($"[data-testid=role-edit-{_roles.Roles.First().Id}]").ClickAsync(new());
        await cut.WaitForElement("[data-testid=role-save]").ClickAsync(new());

        Assert.Contains("Could not load data", cut.WaitForElement("[data-testid=role-error]").TextContent);
    }

    [Fact]
    public async Task Delete_offline_shows_a_snackbar_instead_of_crashing()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Delete);
        var custom = await _roles.CreateRoleAsync(new CreateRoleRequest("دور", "Custom", RoleBaseKinds.Staff, []));
        var cut = RenderPage();
        _roles.NextFailure = new HttpRequestException("offline");

        var click = cut.WaitForElement($"[data-testid=role-delete-{custom.Id}]").ClickAsync(new());
        await _dialogs.WaitForElement("[data-testid=confirm-yes]").ClickAsync(new());
        await click;

        cut.WaitForAssertion(() => Assert.Contains("Could not load data", _snackbars.Markup));
    }

    [Fact]
    public async Task Page_renders_rtl_in_arabic()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("ar");
        await GrantAsync(Permissions.Roles.View);
        Render<MudPopoverProvider>();
        var cut = Render<MudRTLProvider>(p => p
            .Add(r => r.RightToLeft, true)
            .AddChildContent<RolesPage>());

        cut.WaitForElement("table");
        Assert.Contains("الأدوار والصلاحيات", cut.Markup);
        Assert.Contains(BuiltInRoles.All[0].SchoolGroupLabel.NameAr, cut.Markup);
    }
}
