using Lms.Domain.Common;

namespace Lms.Domain.Content;

/// <summary>
/// A student's progress on one module (FR-CNT-04). Not audited row by row (volume); written only by
/// <see cref="ICompletionRecorder"/>. One row per (module, student).
/// </summary>
public sealed class ModuleCompletion : Entity, IOrgUnitScoped
{
    // The same values as Lms.Shared.Content.CompletionStates (a unit test pins them).
    public const string NotStartedState = "NotStarted";
    public const string InProgressState = "InProgress";
    public const string CompletedState = "Completed";

    private ModuleCompletion()
    {
        OrgUnitPath = State = string.Empty;
    }

    public ModuleCompletion(LessonModule module, Guid studentId, string state, DateTime nowUtc)
    {
        OrganizationId = module.OrganizationId;
        OrgUnitId = module.OrgUnitId;
        OrgUnitPath = module.OrgUnitPath;
        ModuleId = module.Id;
        ClassCourseId = module.ClassCourseId;
        StudentId = studentId;
        State = state;
        UpdatedAtUtc = Micros(nowUtc);
        if (state == CompletedState)
        {
            CompletedAtUtc = Micros(nowUtc);
        }
    }

    public Guid OrganizationId { get; private set; }
    public Guid OrgUnitId { get; private set; }
    public string OrgUnitPath { get; private set; }
    public Guid ModuleId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid StudentId { get; private set; }
    public string State { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public decimal? Score { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>MySql keeps microseconds: the value the caller sees is the value that is stored.</summary>
    private static DateTime Micros(DateTime value) => new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);

    /// <summary>Never downgrades: a completed module stays completed.</summary>
    public bool MoveTo(string state, DateTime nowUtc, decimal? score = null)
    {
        if (State == CompletedState && state != CompletedState)
        {
            return false;
        }

        var changed = State != state || score != Score;
        State = state;
        UpdatedAtUtc = Micros(nowUtc);
        if (score is not null)
        {
            Score = score;
        }

        if (state == CompletedState && CompletedAtUtc is null)
        {
            CompletedAtUtc = Micros(nowUtc);
            changed = true;
        }

        return changed;
    }
}
