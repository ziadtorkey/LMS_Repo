using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Enrollment;

/// <summary>A group is a valid target when it is live (not deleted), in the caller's scope, and belongs to the class course.</summary>
public sealed class GroupTargets(LmsDbContext db) : IGroupTargets
{
    public Task<bool> IsValidTargetAsync(Guid classCourseId, Guid groupId, CancellationToken cancellationToken) =>
        db.Set<StudentGroup>().AnyAsync(g => g.Id == groupId && g.ClassCourseId == classCourseId, cancellationToken);
}
