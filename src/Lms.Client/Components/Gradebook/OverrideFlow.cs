using Lms.Client.Resources;
using Lms.Shared.Grading;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Lms.Client.Components.Gradebook;

/// <summary>Asks for the new points and the required reason of an override, then saves it through the edit session. Shared by the grid and the phone list.</summary>
public static class OverrideFlow
{
    public static async Task RunAsync(IDialogService dialogs, IStringLocalizer<SharedResource> l, GradebookEditSession session, GradeRowDto row, GradeItemDto item,
        string studentName, string itemTitle)
    {
        var current = row.Cells.FirstOrDefault(c => c.ItemId == item.Id)?.Points;
        var dialog = await dialogs.ShowAsync<OverrideReasonDialog>(l["Grd_Override_Title"], new DialogParameters<OverrideReasonDialog>
        {
            { x => x.StudentName, studentName }, { x => x.ItemTitle, itemTitle }, { x => x.Max, item.MaxPoints }, { x => x.Current, current },
        }, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true });
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: ValueTuple<decimal, string> chosen })
            await session.OverrideAsync(row, item, chosen.Item1, chosen.Item2);
    }
}
