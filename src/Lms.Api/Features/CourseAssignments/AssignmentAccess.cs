using Lms.Api.Features.Content;
using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.CourseAssignments;

/// <summary>Student-side checks shared by the assignment handlers and the asset reader (D51: one visibility resolver).</summary>
internal static class AssignmentAccess
{
    /// <summary>The module is visible now to the calling student (published or in its window, for their class course or group).</summary>
    public static async Task<bool> StudentSeesAsync(LmsDbContext db, ModuleAccess access, LessonModule module, DateTime nowUtc, CancellationToken ct)
    {
        var studentId = await access.StudentIdAsync(ct);
        if (studentId is null)
        {
            return false;
        }

        var group = await access.GroupOfAsync(module.ClassCourseId, studentId.Value, ct);
        return await access.VisibleTo(module.ClassCourseId, group, nowUtc).AnyAsync(p => p.Module.Id == module.Id, ct);
    }

    /// <summary>The student has an active enrollment in the class course.</summary>
    public static Task<bool> EnrolledAsync(LmsDbContext db, Guid classCourseId, Guid studentId, CancellationToken ct) =>
        db.Set<CourseEnrollment>().AnyAsync(
            e => e.ClassCourseId == classCourseId && e.StudentId == studentId && e.Status == CourseEnrollment.ActiveStatus, ct);
}
