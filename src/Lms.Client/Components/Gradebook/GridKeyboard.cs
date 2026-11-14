namespace Lms.Client.Components.Gradebook;

public enum GridAction
{
    None,
    Move,
    StartEdit,
    CommitStay,
    CommitMoveNext,
    CommitMovePrevious,
    CancelEdit,
    OpenMenu,
}

/// <summary>What a key does in the grid. <see cref="Row"/> and <see cref="Col"/> are the cell to go to (the same cell when nothing moves).</summary>
public readonly record struct GridKeyResult(GridAction Action, int Row, int Col)
{
    public static GridKeyResult Stay(int row, int col) => new(GridAction.None, row, col);
}

/// <summary>
/// The key map of the gradebook grid (L24, spec 2.3). Pure logic, so it is tested without a browser; the Blazor grid calls it and the small JS module only
/// stops the page from scrolling or tabbing on the keys this map handles.
/// <list type="bullet">
/// <item>Navigation: arrows move between cells (in a right-to-left page the left and right arrows follow the reading direction), Home and End go to the
/// ends of the row, Ctrl+Home to the first cell. Tab is not handled, so it leaves the grid.</item>
/// <item>Enter or F2 starts editing a cell that can be edited. While editing: Enter commits and stays, Tab commits and moves to the next cell, Shift+Tab to
/// the previous one, Esc cancels, F2 commits (it toggles).</item>
/// <item>Shift+F10 or the context-menu key opens the cell menu.</item>
/// </list>
/// </summary>
public static class GridKeyboard
{
    public static GridKeyResult Handle(string key, bool shift, bool ctrl, bool alt, int row, int col, int rows, int cols, bool rtl, bool editing, bool editable)
    {
        if (alt) return GridKeyResult.Stay(row, col);
        var last = Math.Max(0, cols - 1);
        var lastRow = Math.Max(0, rows - 1);

        if (editing)
        {
            return key switch
            {
                "Enter" or "F2" => new GridKeyResult(GridAction.CommitStay, row, col),
                "Tab" when shift => new GridKeyResult(GridAction.CommitMovePrevious, row, Math.Max(0, col - 1)),
                "Tab" => new GridKeyResult(GridAction.CommitMoveNext, row, Math.Min(last, col + 1)),
                "Escape" => new GridKeyResult(GridAction.CancelEdit, row, col),
                _ => GridKeyResult.Stay(row, col),
            };
        }

        var forward = rtl ? -1 : 1; // the arrow that points "right" moves one column back in an Arabic page
        return key switch
        {
            "ArrowDown" => Move(row + 1, col),
            "ArrowUp" => Move(row - 1, col),
            "ArrowRight" => Move(row, col + forward),
            "ArrowLeft" => Move(row, col - forward),
            "Home" when ctrl => Move(0, 0),
            "Home" => Move(row, 0),
            "End" => Move(row, last),
            "Enter" or "F2" => editable ? new GridKeyResult(GridAction.StartEdit, row, col) : GridKeyResult.Stay(row, col),
            "ContextMenu" => new GridKeyResult(GridAction.OpenMenu, row, col),
            "F10" when shift => new GridKeyResult(GridAction.OpenMenu, row, col),
            _ => GridKeyResult.Stay(row, col),
        };

        GridKeyResult Move(int r, int c) => new(GridAction.Move, Math.Clamp(r, 0, lastRow), Math.Clamp(c, 0, last));
    }

    /// <summary>Whether the page's default for this key (scrolling, tabbing, the browser menu) must be stopped: the keys this map handles in this mode.</summary>
    public static bool HandlesKey(string key, bool shift, bool editing) => editing
        ? key is "Enter" or "Tab" or "Escape" or "F2"
        : key is "ArrowUp" or "ArrowDown" or "ArrowLeft" or "ArrowRight" or "Home" or "End" or "Enter" or "F2" or "ContextMenu" || (key == "F10" && shift);
}
