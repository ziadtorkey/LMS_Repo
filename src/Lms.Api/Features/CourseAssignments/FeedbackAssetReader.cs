using Lms.Api.Features.Content;
using Lms.Api.Infrastructure;
using Lms.Domain.Assignments;
using Lms.Domain.Content;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>
/// Lets the annotated file of a grade be read (M4a Task 9): the student who owns the submission, once it is returned (never
/// before, never another student's), and staff who may grade the class course. Only files that a feedback row really references.
/// </summary>
public sealed class FeedbackAssetReader(IHttpContextAccessor accessor, LmsDbContext db, ModuleAccess access) : IAssetReferenceReader
{
    public async Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (asset.ClassCourseId is not { } classCourseId)
        {
            return false;
        }

        var feedback = db.Set<SubmissionFeedback>().Where(f => f.AnnotatedFileAssetId == asset.Id && f.ClassCourseId == classCourseId);
        if (await access.IsStudentAsync(cancellationToken))
        {
            if (await access.StudentIdAsync(cancellationToken) is not { } studentId)
            {
                return false;
            }

            return await (from f in feedback
                          join s in db.Set<Submission>() on f.SubmissionId equals s.Id
                          where s.StudentId == studentId && s.Status == Submission.ReturnedStatus
                          select f.Id).AnyAsync(cancellationToken);
        }

        return accessor.HttpContext is { } http
               && (await http.CheckClassCourseAsync(Permissions.CourseAssignments.Grade, classCourseId, cancellationToken)).Problem is null
               && await feedback.AnyAsync(cancellationToken);
    }
}
