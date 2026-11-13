namespace Lms.Domain.Packages;

/// <summary>
/// Recomputes the graded score of a package module after its grading method changed (D57): from the students' completed attempts under
/// the new method, writing a grade event only where the score changes. The attempts arrive with the tracking task (T9b), which replaces the
/// no-op registered until then.
/// </summary>
public interface IPackageGradeRecompute
{
    Task RecomputeAsync(Guid packageId, CancellationToken ct = default);
}
