using MudBlazor;

namespace Lms.Client.Services;

public static class DialogFlow
{
    /// <summary>
    /// Opens a form dialog and runs <paramref name="onSaved"/> when it closes with success. The result is not awaited
    /// by the caller, so the click handler returns while the dialog is open (awaiting it would also deadlock bUnit tests).
    /// </summary>
    public static async Task ShowAsync<TDialog>(
        IDialogService dialogs, string title, DialogParameters<TDialog> parameters, Func<Task> onSaved)
        where TDialog : Microsoft.AspNetCore.Components.IComponent
    {
        var reference = await dialogs.ShowAsync<TDialog>(title, parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        _ = AfterAsync(reference, onSaved);
    }

    private static async Task AfterAsync(IDialogReference reference, Func<Task> onSaved)
    {
        var result = await reference.Result;
        if (result is { Canceled: false }) await onSaved();
    }
}
