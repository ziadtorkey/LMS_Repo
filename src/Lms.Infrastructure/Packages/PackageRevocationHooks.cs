using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Packages;

/// <summary>
/// The places that must refuse already issued package tokens (ADR 0005 section 5, T21 4.5): a package module that is no longer visible, and a
/// student whose enrollment ends. Each adds <see cref="PackageRevocation"/> markers to the caller's unit of work (the caller saves).
/// </summary>
public static class PackageRevocationHooks
{
    /// <summary>Revokes every token of the package behind a SCORM/H5P module that is not visible now.</summary>
    public static async Task ModuleChangedAsync(LmsDbContext db, LessonModule module, DateTime nowUtc, CancellationToken ct)
    {
        if (module.RefId is not { } packageId || module.Kind is not (ModuleKinds.Scorm or ModuleKinds.H5p) || module.IsVisibleNow(nowUtc))
        {
            return;
        }

        var package = await db.Set<PackageUpload>().FirstOrDefaultAsync(p => p.Id == packageId, ct);
        if (package is not null)
        {
            db.Add(new PackageRevocation(package, null, nowUtc));
        }
    }

    /// <summary>Revokes the tokens of the students of the dropped enrollments for every package of their class courses.</summary>
    public static async Task EnrollmentsDroppedAsync(LmsDbContext db, IReadOnlyCollection<CourseEnrollment> enrollments, DateTime nowUtc, CancellationToken ct)
    {
        foreach (var byCourse in enrollments.GroupBy(e => e.ClassCourseId))
        {
            var packages = await db.Set<PackageUpload>().Where(p => p.ClassCourseId == byCourse.Key).ToListAsync(ct);
            foreach (var package in packages)
            {
                foreach (var studentId in byCourse.Select(e => e.StudentId).Distinct())
                {
                    db.Add(new PackageRevocation(package, studentId, nowUtc));
                }
            }
        }
    }
}
