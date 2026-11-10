using Bunit;
using Lms.Client.Components;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Lms.Client.Tests.Components;

public sealed class ConfirmDialogTests : ClientTestContext
{
    private async Task<(IRenderedComponent<MudDialogProvider> Provider, Task<bool> Answer)> OpenAsync()
    {
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var answer = ConfirmDialog.AskAsync(dialogs, "Delete?", "Really delete?");
        provider.WaitForElement("[data-testid=confirm-yes]");
        await Task.CompletedTask;
        return (provider, answer);
    }

    [Fact]
    public async Task Confirm_dialog_returns_true_only_on_confirm()
    {
        var (provider, answer) = await OpenAsync();
        await provider.Find("[data-testid=confirm-yes]").ClickAsync(new());
        Assert.True(await answer);
    }

    [Fact]
    public async Task Cancel_returns_false()
    {
        var (provider, answer) = await OpenAsync();
        await provider.Find("[data-testid=confirm-no]").ClickAsync(new());
        Assert.False(await answer);
    }
}
