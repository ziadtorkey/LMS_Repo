using Bunit;
using Lms.Client.Components.People;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Shared.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Support;

/// <summary>Shared setup of the M2 people tests: fake staff, roles, units and file upload APIs plus the MudBlazor providers.</summary>
public abstract class StaffTestContext : ClientTestContext
{
    protected FakeAccessAdminApi Access { get; } = new();
    protected FakeFileUploadApi Files { get; } = new();
    protected IRenderedComponent<MudDialogProvider> Dialogs { get; private set; } = default!;
    protected IRenderedComponent<MudPopoverProvider> Popovers { get; private set; } = default!;

    protected StaffTestContext()
    {
        Services.AddSingleton<IAccessAdminApi>(Access);
        Services.AddSingleton<IFileUploadApi>(Files);
        Services.AddSingleton<IUnitLookupApi, FakeUnitLookupApi>();
    }

    protected void RenderProviders()
    {
        Popovers = Render<MudPopoverProvider>();
        Dialogs = Render<MudDialogProvider>();
        Render<MudSnackbarProvider>();
    }

    protected Guid RoleOfKind(string baseKind) => Access.Roles.First(r => r.BaseKind == baseKind).Id;

    protected new async Task GrantAsync(params string[] permissions)
    {
        Api.Permissions = new(null, permissions);
        await Context.SetAsync(Guid.NewGuid());
        await Services.GetRequiredService<PermissionState>().RefreshAsync();
    }

    /// <summary>Sets a select's value the way a user choosing an item would (opening the popover is not needed).</summary>
    protected static async Task ChooseAsync<T>(IRenderedComponent<MudSelect<T>> select, T value) =>
        await select.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(value));

    protected static IRenderedComponent<MudSelect<Guid?>> SelectWithTestId(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> root, string testId) =>
        root.FindComponents<MudSelect<Guid?>>().First(s => s.Instance.UserAttributes.TryGetValue("data-testid", out var v) && (string?)v == testId);
}
