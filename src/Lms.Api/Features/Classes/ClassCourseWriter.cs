using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>
/// Adds courses to a class and runs the lifecycle handlers (enrollment engine, later modules) in the caller's
/// transaction: the class course rows are written first so handlers can point at them, then the handlers run.
/// </summary>
public sealed class ClassCourseWriter(
    LmsDbContext db, ICourseCatalog catalog, IEnumerable<IClassCourseLifecycleHandler> handlers)
{
    /// <summary>The course exists for the caller, is Active and is allocated at (or above) the unit.</summary>
    public async Task<Course?> FindAvailableAsync(Guid courseId, OrgUnit unit, CancellationToken ct)
    {
        var view = await catalog.FindAsync(courseId, ct);
        return view is { Course.Status: Course.ActiveStatus } && await catalog.IsAvailableAtAsync(courseId, unit, ct) ? view.Course : null;
    }

    /// <summary>Saves the new class courses and calls <c>OnAddedAsync</c> for each; the caller owns the transaction.</summary>
    public async Task<IReadOnlyList<ClassCourse>> AddAsync(
        Class cls, IEnumerable<(Course Course, bool IsOptional)> courses, CancellationToken ct)
    {
        var added = courses.Select(c => new ClassCourse(cls, c.Course, c.IsOptional)).ToList();
        if (added.Count == 0)
        {
            return added;
        }

        db.AddRange(added);
        await db.SaveChangesAsync(ct);
        foreach (var classCourse in added)
        {
            foreach (var handler in handlers)
            {
                await handler.OnAddedAsync(classCourse, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task ChangedOptionalAsync(ClassCourse classCourse, CancellationToken ct)
    {
        foreach (var handler in handlers)
        {
            await handler.OnOptionalChangedAsync(classCourse, ct);
        }
    }

    /// <summary>Asks every handler; a <see cref="ClassCourseInUseException"/> vetoes the removal.</summary>
    public async Task RemovingAsync(ClassCourse classCourse, CancellationToken ct)
    {
        foreach (var handler in handlers)
        {
            await handler.OnRemovingAsync(classCourse, ct);
        }
    }

    public Task<bool> ExistsAsync(Guid classId, Guid courseId, CancellationToken ct) =>
        db.Set<ClassCourse>().AnyAsync(c => c.ClassId == classId && c.CourseId == courseId, ct);
}
