using System.Globalization;
using Bunit;
using Lms.Client.Pages.Access;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Lms.Shared.Access;
using Lms.Shared.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Pages.Access;

/// <summary>
/// Regression tests for the roles page and its permission matrix, written against the page as it is on main so that a restyle
/// (the Stitch roles matrix) cannot silently drop, duplicate or mislabel a permission, change the payload or unlock a built-in role.
/// </summary>
public sealed class RolesMatrixRegressionTests : ClientTestContext
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly FakeAccessAdminApi _roles = new();

    public RolesMatrixRegressionTests()
    {
        Services.AddSingleton<IAccessAdminApi>(_roles);
        Culture.Current = new CultureInfo("en");
    }

    private new async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    private IRenderedComponent<RolesPage> RenderPage()
    {
        Render<MudPopoverProvider>();
        Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
        return Render<RolesPage>();
    }

    private async Task<IRenderedComponent<RolesPage>> OpenNewRoleAsync()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Create);
        var cut = RenderPage();
        await cut.WaitForElement("[data-testid=role-new]").ClickAsync(new());
        cut.WaitForElement("[data-testid=role-save]", Wait);
        return cut;
    }

    private static void Check(IRenderedComponent<RolesPage> cut, string permission, bool on = true) =>
        cut.Find($"[data-testid=\"perm-{permission}\"]").Change(on);

    // ---- 1. the matrix shows exactly the assignable permissions ----

    [Fact]
    public async Task The_matrix_renders_exactly_the_assignable_permissions_once_each()
    {
        var cut = await OpenNewRoleAsync();

        var rendered = cut.FindAll("[data-testid^=perm-]").Select(e => e.GetAttribute("data-testid")!["perm-".Length..]).ToList();
        var expected = PermissionGroups.Assignable.SelectMany(g => g.Permissions).ToList();

        Assert.Empty(rendered.GroupBy(p => p).Where(g => g.Count() > 1).Select(g => g.Key));          // none duplicated
        Assert.Empty(expected.Except(rendered));                                                       // none missing
        Assert.Empty(rendered.Except(expected));                                                       // none extra (no platform-only permission)
        Assert.Equal(expected.Count, rendered.Count);
        Assert.DoesNotContain(Permissions.Clients.View, rendered);
    }

    [Fact]
    public async Task Each_module_block_holds_exactly_its_own_permissions_and_a_select_all()
    {
        var cut = await OpenNewRoleAsync();

        foreach (var group in PermissionGroups.Assignable)
        {
            var block = cut.Find($"[data-testid=module-{group.Module}]");
            var inBlock = block.QuerySelectorAll("[data-testid^=perm-]").Select(e => e.GetAttribute("data-testid")!["perm-".Length..]).ToList();

            Assert.Equal(group.Permissions.OrderBy(p => p, StringComparer.Ordinal), inBlock.OrderBy(p => p, StringComparer.Ordinal));
            Assert.NotNull(block.QuerySelector($"[data-testid=select-module-{group.Module}]"));
        }
        Assert.Equal(PermissionGroups.Assignable.Count, cut.FindAll("[data-testid^=module-]").Count);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    public async Task Every_checkbox_has_a_visible_label_in_both_languages(string culture)
    {
        Culture.Current = new CultureInfo(culture);
        var cut = await OpenNewRoleAsync();

        foreach (var box in cut.FindAll("[data-testid^=perm-]"))
        {
            var label = box.Closest("label")?.TextContent.Trim() ?? "";
            Assert.False(string.IsNullOrWhiteSpace(label), $"{box.GetAttribute("data-testid")} has no label");
            Assert.DoesNotContain("Perm_", label);   // a missing resource key would show its name
        }
    }

    // ---- 2. toggling and saving sends exactly the expected payload ----

    [Fact]
    public async Task Saving_a_new_role_sends_exactly_the_checked_permissions()
    {
        var cut = await OpenNewRoleAsync();
        cut.Find("[data-testid=name-ar]").Change("منسق");
        cut.Find("[data-testid=name-en]").Change("Coordinator");

        Check(cut, Permissions.Audit.View);
        Check(cut, Permissions.Roles.View);
        Check(cut, Permissions.Students.View);
        Check(cut, Permissions.Roles.View, on: false);   // toggled off again: must not be sent
        await cut.Find("[data-testid=role-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotNull(_roles.LastCreate), Wait);
        var sent = _roles.LastCreate!;
        Assert.Equal(new[] { Permissions.Audit.View, Permissions.Students.View }.OrderBy(p => p, StringComparer.Ordinal), sent.Permissions.OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(sent.Permissions.Count, sent.Permissions.Distinct().Count());
        Assert.Equal("منسق", sent.NameAr);
        Assert.Equal("Coordinator", sent.NameEn);
    }

    [Fact]
    public async Task Select_all_in_a_module_sends_all_of_its_permissions_and_nothing_else()
    {
        var cut = await OpenNewRoleAsync();
        cut.Find("[data-testid=name-ar]").Change("دور");
        cut.Find("[data-testid=name-en]").Change("Role");
        var group = PermissionGroups.Assignable.First(g => g.Module == "Roles");

        await cut.Find("[data-testid=select-module-Roles]").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("[data-testid=role-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotNull(_roles.LastCreate), Wait);
        Assert.Equal(group.Permissions.OrderBy(p => p, StringComparer.Ordinal), _roles.LastCreate!.Permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Editing_a_custom_role_sends_the_changed_set_and_keeps_the_rest()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update);
        var custom = await _roles.CreateRoleAsync(new CreateRoleRequest("دور", "Custom", RoleBaseKinds.Staff, [Permissions.Audit.View, Permissions.Students.View]));
        var cut = RenderPage();

        await cut.WaitForElement($"[data-testid=role-edit-{custom.Id}]").ClickAsync(new());
        cut.WaitForElement("[data-testid=role-save]", Wait);
        Check(cut, Permissions.Audit.View, on: false);
        Check(cut, Permissions.Roles.View);
        await cut.Find("[data-testid=role-save]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.NotNull(_roles.LastUpdate), Wait);
        Assert.Equal(new[] { Permissions.Roles.View, Permissions.Students.View }.OrderBy(p => p, StringComparer.Ordinal), _roles.LastUpdate!.Permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    // ---- 3. built-in roles stay locked ----

    [Fact]
    public async Task Every_built_in_role_is_marked_and_cannot_be_deleted_from_the_page()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update, Permissions.Roles.Delete);
        var custom = await _roles.CreateRoleAsync(new CreateRoleRequest("دور", "Custom", RoleBaseKinds.Staff, []));
        var cut = RenderPage();
        cut.WaitForElement($"[data-testid=role-delete-{custom.Id}]", Wait);
        var builtIn = _roles.Roles.Where(r => r.IsBuiltIn).ToList();

        Assert.NotEmpty(builtIn);
        foreach (var role in builtIn)
        {
            Assert.NotEmpty(cut.FindAll($"[data-testid=role-builtin-{role.Id}]"));
            Assert.True(cut.Find($"[data-testid=role-delete-{role.Id}]").HasAttribute("disabled"), $"{role.NameEn} can be deleted");
        }
        Assert.False(cut.Find($"[data-testid=role-delete-{custom.Id}]").HasAttribute("disabled"));   // the lock is for built-in roles only
    }

    [Fact]
    public async Task The_base_kind_of_every_built_in_role_is_fixed_in_the_editor()
    {
        await GrantAsync(Permissions.Roles.View, Permissions.Roles.Update);
        var cut = RenderPage();

        foreach (var role in _roles.Roles.Where(r => r.IsBuiltIn))
        {
            await cut.WaitForElement($"[data-testid=role-edit-{role.Id}]", Wait).ClickAsync(new());
            cut.WaitForElement("[data-testid=role-basekind-fixed]", Wait);
            Assert.Empty(cut.FindAll("[data-testid=role-basekind]"));
            await cut.Find("[data-testid=role-cancel]").ClickAsync(new());
        }
    }

    [Fact]
    public async Task The_server_refuses_to_delete_a_built_in_role()
    {
        var builtIn = _roles.Roles.First(r => r.IsBuiltIn);

        var ex = await Assert.ThrowsAsync<ApiProblemException>(() => _roles.DeleteRoleAsync(builtIn.Id));

        Assert.Equal("built_in_role", ex.Code);
    }
}
