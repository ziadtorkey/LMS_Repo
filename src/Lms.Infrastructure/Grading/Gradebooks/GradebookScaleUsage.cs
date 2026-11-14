using Lms.Domain.Grading;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>A scale a gradebook points at cannot be deleted (<c>scale_in_use</c>, M5 L7).</summary>
public sealed class GradebookScaleUsage(LmsDbContext db) : IScaleUsage
{
    public Task<bool> IsInUseAsync(Guid scaleId, CancellationToken cancellationToken) =>
        db.Set<Gradebook>().AnyAsync(g => g.GradingScaleId == scaleId, cancellationToken);
}
