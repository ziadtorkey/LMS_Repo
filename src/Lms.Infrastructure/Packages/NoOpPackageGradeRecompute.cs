using Lms.Domain.Packages;

namespace Lms.Infrastructure.Packages;

/// <summary>There are no attempts yet (T9b), so a changed grading method has nothing to recompute.</summary>
public sealed class NoOpPackageGradeRecompute : IPackageGradeRecompute
{
    public Task RecomputeAsync(Guid packageId, CancellationToken ct = default) => Task.CompletedTask;
}
