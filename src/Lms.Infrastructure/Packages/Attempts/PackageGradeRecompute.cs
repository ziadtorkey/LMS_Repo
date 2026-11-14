using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Packages.Attempts;

/// <summary>
/// The D57 recompute after a change of the grading method: every student with a completed attempt is re-graded under the new method
/// and a grade event is written only where the graded score changes (replaces the no-op of the grading-method task).
/// </summary>
public sealed class PackageGradeRecompute(LmsDbContext db, PackageAttemptService attempts) : IPackageGradeRecompute
{
    public async Task RecomputeAsync(Guid packageId, CancellationToken ct = default)
    {
        var package = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == packageId, ct);
        if (package is not null)
        {
            await attempts.RegradeAllAsync(package, ct);
        }
    }
}
