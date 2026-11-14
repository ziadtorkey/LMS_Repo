using Lms.Domain.Attendance;

namespace Lms.Infrastructure.Attendance;

/// <summary>One student mark of a take call.</summary>
public sealed record TakeMark(Guid StudentId, string Status, string? Note);

/// <summary>The outcome of planning a take: the final status per roster student, or the problem code that refuses it (with the HTTP meaning in <see cref="TakeProblem"/>).</summary>
public sealed record TakePlan(IReadOnlyDictionary<Guid, (string Status, string? Note)> Final, string? ProblemCode, int ProblemStatus)
{
    public bool Ok => ProblemCode is null;

    public static TakePlan Refused(string code, int status) => new(new Dictionary<Guid, (string, string?)>(), code, status);
}

/// <summary>
/// The rules of taking attendance (FR-ATT-01, L12, L14), kept free of HTTP and storage so they are unit tested: the default plus exceptions, no unmarked rows on a
/// Taken session, no future session, and the edit window.
/// </summary>
public static class AttendanceTaker
{
    private static readonly string[] Statuses = ["Present", "Absent", "Late", "Excused"];

    public static bool InWindow(DateTime? takenAtUtc, int windowHours, DateTime nowUtc) => takenAtUtc is null || nowUtc <= takenAtUtc.Value.AddHours(windowHours);

    public static TakePlan Plan(
        IReadOnlyCollection<Guid> roster, IReadOnlyDictionary<Guid, (string? Status, string? Note)> existing, string? defaultStatus, IReadOnlyList<TakeMark> marks,
        bool sessionTaken, bool inWindow, bool canEditAfterWindow, string? reason, DateOnly sessionDate, DateOnly today)
    {
        if (sessionDate > today)
        {
            return TakePlan.Refused("attendance_session_future", 409);
        }

        if (defaultStatus is not null && !Statuses.Contains(defaultStatus) || marks.Any(m => !Statuses.Contains(m.Status)))
        {
            return TakePlan.Refused("validation_failed", 400);
        }

        var rosterSet = roster.ToHashSet();
        if (marks.Any(m => !rosterSet.Contains(m.StudentId)) || marks.Select(m => m.StudentId).Distinct().Count() != marks.Count)
        {
            return TakePlan.Refused("validation_failed", 400);
        }

        if (reason is { } text && text.Trim().Length > 500)
        {
            return TakePlan.Refused("validation_failed", 400);
        }

        if (sessionTaken && !inWindow)
        {
            if (!canEditAfterWindow)
            {
                return TakePlan.Refused("attendance_edit_window_closed", 409);
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                return TakePlan.Refused("attendance_reason_required", 422);
            }
        }

        var explicitMarks = marks.ToDictionary(m => m.StudentId);
        var final = new Dictionary<Guid, (string, string?)>();
        foreach (var student in roster)
        {
            if (explicitMarks.TryGetValue(student, out var mark))
            {
                final[student] = (mark.Status, mark.Note);
            }
            else if (defaultStatus is not null)
            {
                final[student] = (defaultStatus, existing.GetValueOrDefault(student).Note);
            }
            else if (existing.TryGetValue(student, out var current) && current.Status is { } status)
            {
                final[student] = (status, current.Note);
            }
            else
            {
                return TakePlan.Refused("attendance_unmarked_rows", 422);
            }
        }

        return new TakePlan(final, null, 200);
    }

    /// <summary>The history kind of a change: the first take, an edit inside the window, or an edit after it.</summary>
    public static string ChangeKind(bool sessionWasTaken, bool inWindow) =>
        !sessionWasTaken ? "Taken" : inWindow ? "Edited" : "EditedAfterWindow";
}
