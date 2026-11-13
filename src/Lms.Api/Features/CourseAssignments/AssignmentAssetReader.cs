using Lms.Api.Features.Content;
using Lms.Domain.Abstractions;
using Lms.Domain.Assignments;
using Lms.Domain.Content;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>
/// Lets a student read the assets that assignments reference (M4a Task 8): the files of their own submission and the attachments
/// of an assignment they can see. Never another student's submission file. Staff read through management of the class course.
/// </summary>
public sealed class AssignmentAssetReader(LmsDbContext db, ModuleAccess access, IClock clock) : IAssetReferenceReader
{
    public async Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (!await access.IsStudentAsync(cancellationToken) || await access.StudentIdAsync(cancellationToken) is not { } studentId)
        {
            return false;
        }

        var own = await (from f in db.Set<SubmissionFile>()
                         join s in db.Set<Submission>() on f.SubmissionId equals s.Id
                         where f.AssetId == asset.Id && s.StudentId == studentId
                         select f.Id).AnyAsync(cancellationToken);
        if (own)
        {
            return true;
        }

        var assignmentIds = await db.Set<AssignmentAttachment>().Where(t => t.AssetId == asset.Id).Select(t => t.AssignmentId)
            .ToListAsync(cancellationToken);
        foreach (var assignmentId in assignmentIds)
        {
            var module = await db.Set<LessonModule>().FirstOrDefaultAsync(
                m => m.Kind == ModuleKinds.Assignment && m.RefId == assignmentId, cancellationToken);
            if (module is not null
                && await AssignmentAccess.EnrolledAsync(db, module.ClassCourseId, studentId, cancellationToken)
                && await AssignmentAccess.StudentSeesAsync(db, access, module, clock.UtcNow, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
