using System.Globalization;
using System.Text;
using Lms.Client.Resources;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Services.Grading;
using Lms.Shared;
using Lms.Shared.Grading;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Components.Gradebook;

public enum CellStatus
{
    Saving,
    Failed,
}

/// <summary>A cell the user changed and the server has not confirmed (or refused). The shown value is the user's until the server row replaces it.</summary>
public sealed record CellUi(CellStatus Status, decimal? Value, string? Error);

/// <summary>
/// The edits of one gradebook page: optimistic values, saving and failed states, the retry of a failed cell, and the two live-region messages. It lives in the
/// page (not in a layout), so a half-typed or unsaved change survives crossing the 600 px breakpoint that swaps the grid for the phone list. The server row
/// always replaces the optimistic value (<see cref="RowReplaced"/>); the client never calculates a grade (L18).
/// </summary>
/// <summary>How a typed score ended: saved, refused before any call (stay in the cell and fix it) or failed on the server (the cell shows why).</summary>
public enum SaveResult
{
    Saved,
    Invalid,
    Failed,
}

public sealed class GradebookEditSession(IGradebookApi api, Guid classCourseId, IStringLocalizer<SharedResource> l, Func<Task> reloadRows)
{
    private readonly Dictionary<(Guid Student, Guid Item), CellUi> _cells = [];
    private readonly Dictionary<(Guid Student, Guid Item), Func<Task>> _retries = [];
    private readonly Dictionary<Guid, int> _stamps = [];
    private int _announcement;

    /// <summary>Raised with the row the server answered; the page swaps it into the list so only that row renders again.</summary>
    public event Action<GradeRowDto>? RowReplaced;

    /// <summary>Raised when anything the session shows changed (a cell state, a message).</summary>
    public event Action? Changed;

    /// <summary>Saves are announced here, politely. The counter makes the same text announce again.</summary>
    public string Polite { get; private set; } = "";

    /// <summary>A rejected value is announced here, assertively, once.</summary>
    public string Assertive { get; private set; } = "";

    public int Announcement => _announcement;

    /// <summary>Changes whenever anything for this student changed: the row component compares it to skip rendering when nothing did.</summary>
    public int Stamp(Guid student) => _stamps.GetValueOrDefault(student);

    public CellUi? Cell(Guid student, Guid item) => _cells.GetValueOrDefault((student, item));

    /// <summary>The cell being edited (in the grid or in the phone list) and what is typed in it. Both layouts read it, so a half-typed score survives a breakpoint change.</summary>
    public (Guid Student, Guid Item)? Editing { get; private set; }

    public string Draft { get; private set; } = "";

    public void BeginEdit(Guid student, Guid item, string initial)
    {
        var before = Editing;
        Editing = (student, item);
        Draft = initial;
        if (before is { } b && b.Student != student) Touch(b.Student);
        Touch(student);
        Changed?.Invoke();
    }

    /// <summary>Records the typing. It does not render any row again: the input already shows what was typed.</summary>
    public void SetDraft(string value) => Draft = value;

    public void EndEdit()
    {
        if (Editing is not { } e) return;
        Editing = null;
        Draft = "";
        Touch(e.Student);
        Changed?.Invoke();
    }

    public void Say(string text)
    {
        Polite = text;
        _announcement++;
        Changed?.Invoke();
    }

    private void Reject(string text)
    {
        Assertive = text;
        _announcement++;
        Changed?.Invoke();
    }

    private void Touch(Guid student) => _stamps[student] = _stamps.GetValueOrDefault(student) + 1;

    /// <summary>Whether Enter on this cell retries a failed save instead of editing (the cell says "press Enter to try again").</summary>
    public bool CanRetry(Guid student, Guid item) => _retries.ContainsKey((student, item));

    public Task RetryAsync(Guid student, Guid item) => _retries.TryGetValue((student, item), out var retry) ? retry() : Task.CompletedTask;

    /// <summary>Parses what the user typed: Arabic-Indic digits and the Arabic decimal separator are accepted, a comma too.</summary>
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var sb = new StringBuilder();
        foreach (var ch in text.Trim())
        {
            sb.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                '٫' or ',' => '.',
                _ => ch,
            });
        }

        return decimal.TryParse(sb.ToString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// A typed score for a cell. An empty input clears the score, a number is checked against the item's maximum before any call (the server stays the
    /// authority), and an event score is changed through an override, which needs a reason (so a plain edit of one is not offered).
    /// </summary>
    public async Task<SaveResult> SetScoreAsync(GradeRowDto row, GradeItemDto item, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return await ClearAsync(row, item) ? SaveResult.Saved : SaveResult.Failed;
        if (!TryParse(input, out var points))
        {
            Reject(l["Grd_Reject_NotANumber"].Value);
            return SaveResult.Invalid;
        }

        if (points < 0m || points > item.MaxPoints)
        {
            Reject(string.Format(CultureInfo.CurrentCulture, l["Grd_Reject_OutOfRange"].Value, GradeFormat.Points(item.MaxPoints)));
            return SaveResult.Invalid;
        }

        var current = row.Cells.FirstOrDefault(c => c.ItemId == item.Id);
        if (current is { Points: { } p } && p == points) return SaveResult.Saved; // nothing changed
        if (current is { State: GradeCellStates.Override or GradeCellStates.NewerScoreExists, Reason.Length: > 0 })
        {
            // Typing over an overridden score changes the override and keeps its reason (a plain Set would drop the override).
            return await OverrideAsync(row, item, points, current.Reason!) ? SaveResult.Saved : SaveResult.Failed;
        }

        return await SaveAsync(row, item, new GradeEntryChange(item.Id, GradeEntryActions.Set, points, null), points, GradeFormat.Points(points)) ? SaveResult.Saved : SaveResult.Failed;
    }

    public Task<bool> OverrideAsync(GradeRowDto row, GradeItemDto item, decimal points, string reason) =>
        SaveAsync(row, item, new GradeEntryChange(item.Id, GradeEntryActions.Override, points, reason), points, GradeFormat.Points(points));

    public Task<bool> ExcuseAsync(GradeRowDto row, GradeItemDto item) =>
        SaveAsync(row, item, new GradeEntryChange(item.Id, GradeEntryActions.Excuse, null, null), null, null);

    public Task<bool> ClearAsync(GradeRowDto row, GradeItemDto item) =>
        SaveAsync(row, item, new GradeEntryChange(item.Id, GradeEntryActions.Clear, null, null), null, null);

    public Task<bool> KeepOverrideAsync(GradeRowDto row, GradeItemDto item) =>
        SaveAsync(row, item, new GradeEntryChange(item.Id, GradeEntryActions.KeepOverride, null, null), null, null);

    public Task<bool> DropOverrideAsync(GradeRowDto row, GradeItemDto item) =>
        SaveAsync(row, item, new GradeEntryChange(item.Id, GradeEntryActions.DropOverride, null, null), null, null);

    private async Task<bool> SaveAsync(GradeRowDto row, GradeItemDto item, GradeEntryChange change, decimal? shown, string? shownText)
    {
        var key = (row.StudentId, item.Id);
        _retries.Remove(key);
        _cells[key] = new CellUi(CellStatus.Saving, shown, null);
        Touch(row.StudentId);
        Changed?.Invoke();
        try
        {
            var saved = await api.UpsertEntriesAsync(classCourseId, new UpsertEntriesRequest(row.StudentId, row.Version, [change]));
            _cells.Remove(key);
            Touch(row.StudentId);
            RowReplaced?.Invoke(saved);
            Say(l["Grd_Saved"].Value);
            return true;
        }
        catch (ApiProblemException ex) when (ex.Code == ProblemCodes.concurrency_conflict)
        {
            // The row changed on the server since it was loaded: drop the optimistic value and load the row again.
            _cells.Remove(key);
            Touch(row.StudentId);
            await reloadRows();
            Say(l["Grd_Conflict_Reloaded"].Value);
            return false;
        }
        catch (ApiProblemException ex)
        {
            var reason = ex.Code switch
            {
                ProblemCodes.grade_out_of_range => string.Format(CultureInfo.CurrentCulture, l["Grd_Reject_OutOfRange"].Value, GradeFormat.Points(item.MaxPoints)),
                _ => ProblemText.For(ex, l),
            };
            return Fail(row, key, shown, reason, () => SaveAsync(row, item, change, shown, shownText));
        }
        catch (HttpRequestException)
        {
            return Fail(row, key, shown, l["Error_Generic"].Value, () => SaveAsync(row, item, change, shown, shownText));
        }
    }

    /// <summary>A failed save keeps the cell on screen as "not saved" with the reason and the value the user typed; Enter retries it.</summary>
    private bool Fail(GradeRowDto row, (Guid Student, Guid Item) key, decimal? shown, string reason, Func<Task<bool>> retry)
    {
        _cells[key] = new CellUi(CellStatus.Failed, shown, reason);
        _retries[key] = async () => { await retry(); };
        Touch(row.StudentId);
        Reject(string.Format(CultureInfo.CurrentCulture, l["Grd_Not_Saved"].Value, reason));
        return false;
    }

    /// <summary>Forgets a failed cell (the user moved on or reverted): the cell shows the server value again.</summary>
    public void Dismiss(Guid student, Guid item)
    {
        if (!_cells.Remove((student, item))) return;
        _retries.Remove((student, item));
        Touch(student);
        Changed?.Invoke();
    }
}
