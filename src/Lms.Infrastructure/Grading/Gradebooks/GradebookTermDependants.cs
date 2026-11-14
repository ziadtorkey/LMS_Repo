using Lms.Domain.Grading;
using Lms.Infrastructure.AcademicYears;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading.Gradebooks;

/// <summary>A term with a gradebook keeps its structure: the term structure of the year is not changed under it (M5 L8).</summary>
public sealed class GradebookTermDependants(LmsDbContext db) : ITermDependants
{
    public Task<bool> AnyAsync(IReadOnlyCollection<Guid> termIds, CancellationToken ct) =>
        db.Set<Gradebook>().AnyAsync(g => termIds.Contains(g.TermId), ct);
}
