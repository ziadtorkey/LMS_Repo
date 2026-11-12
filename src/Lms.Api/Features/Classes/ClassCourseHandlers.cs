using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Enrollment;
using Lms.Shared.Enrollment;
using Lms.Domain.AcademicYears;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Classes;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Classes;

/// <summary>Class courses: add, list, change optional flag, remove and bulk assign.</summary>
public sealed class ClassCourseHandlers(
    LmsDbContext db, IClock clock, ClassCourseWriter writer, ClassCourseMapper mapper, ClassHandlers classes, ClassLookups lookups,
    IRelationshipScope relationships)
{
    internal const int MaxCoursesPerAdd = 100;
    internal const int MaxBulkAssignClasses = 200;

    public async Task<IResult> AddAsync(Guid classId, AddClassCoursesRequest request, HttpContext http, CancellationToken ct)
    {
        if (request?.CourseIds is not { Count: > 0 } ids || ids.Distinct().Count() != ids.Count)
        {
            return ClassValidators.Invalid(nameof(AddClassCoursesRequest.CourseIds), "courses_required");
        }

        if (ids.Count > MaxCoursesPerAdd)
        {
            return ClassValidators.TooMany(nameof(AddClassCoursesRequest.CourseIds), MaxCoursesPerAdd);
        }

        var (cls, problem) = await http.CheckClassAsync(Permissions.Classes.ManageCourses, classId, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await classes.ClosedProblemAsync(cls!, ct) is { } closed)
        {
            return closed;
        }

        var unit = await db.OrgUnits.FirstAsync(u => u.Id == cls!.OrgUnitId, ct);
        var courses = new List<(Domain.Courses.Course, bool)>();
        foreach (var id in ids)
        {
            // Unknown, inactive, foreign and unallocated courses all answer the same, so nothing is revealed.
            if (await writer.FindAvailableAsync(id, unit, ct) is not { } course)
            {
                return ApiProblems.BadRequest(ProblemCodes.course_not_available, "The course is not available for this class.");
            }

            if (await writer.ExistsAsync(classId, id, ct))
            {
                return ApiProblems.Conflict(ProblemCodes.class_course_exists, "The class already has this course.");
            }

            courses.Add((course, request.IsOptional));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        IReadOnlyList<ClassCourse> added;
        try
        {
            added = await writer.AddAsync(cls!, courses, ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsConcurrentKeyConflict(ex))
        {
            return ApiProblems.Conflict(ProblemCodes.class_course_exists, "A concurrent request already added this course.");
        }

        await transaction.CommitAsync(ct);
        return Results.Created(Routes.ClassCoursesOfClass(classId), await mapper.ToDtosAsync(added, ct));
    }

    public async Task<IResult> ListAsync(Guid classId, HttpContext http, CancellationToken ct)
    {
        var (cls, problem) = await http.CheckClassAsync(Permissions.Classes.View, classId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var query = db.Set<ClassCourse>().Where(c => c.ClassId == cls!.Id);
        var filter = await relationships.ForAsync(Permissions.Classes.View, ct);
        if (!filter.IsUnrestricted)
        {
            var allowed = filter.ClassCourseIds;
            query = query.Where(c => allowed.Contains(c.Id));
        }

        var rows = await query.OrderBy(c => c.CourseId).ToListAsync(ct);
        return Results.Ok(await mapper.ToDtosAsync(rows, ct));
    }

    public async Task<IResult> UpdateAsync(Guid id, UpdateClassCourseRequest request, HttpContext http, CancellationToken ct)
    {
        var (classCourse, problem) = await LoadWritableAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        classCourse!.SetOptional(request.IsOptional);
        await db.SaveChangesAsync(ct);
        await writer.ChangedOptionalAsync(classCourse, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.Ok(await mapper.ToDtoAsync(classCourse, ct));
    }

    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (classCourse, problem) = await LoadWritableAsync(id, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        // D41, enforced here whatever handlers are registered: group members and manual or exceptional (any non-Auto)
        // enrollments block the removal; Auto enrollments follow the class course and become Dropped with it.
        var hasGroupMembers = await db.Set<GroupMember>().AnyAsync(m => m.ClassCourseId == id, ct);
        var enrollments = await db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == id && e.Status == CourseEnrollment.ActiveStatus).ToListAsync(ct);
        if (hasGroupMembers || enrollments.Any(e => e.Source != EnrollmentSources.Auto))
        {
            return ApiProblems.Conflict(
                ProblemCodes.class_course_in_use,
                hasGroupMembers ? "The class course has group members." : "The class course has manual or exceptional enrollments.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        foreach (var enrollment in enrollments)
        {
            enrollment.Drop(today, "ClassCourseRemoved");
        }

        // Saved and detached: a tracked dependent would make EF try to cascade the soft delete of the class course.
        await db.SaveChangesAsync(ct);
        foreach (var enrollment in enrollments)
        {
            db.Entry(enrollment).State = EntityState.Detached;
        }

        try
        {
            await writer.RemovingAsync(classCourse!, ct);
        }
        catch (ClassCourseInUseException ex)
        {
            return ApiProblems.Conflict(ProblemCodes.class_course_in_use, ex.Reason);
        }

        db.Remove(classCourse!);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    public async Task<IResult> BulkAssignAsync(BulkAssignCourseRequest request, HttpContext http, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(Permissions.Classes.ManageCourses, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var yearsById = await lookups.YearsAsync(unit.OrganizationId, [request.AcademicYearId], ct);
        if (!yearsById.TryGetValue(request.AcademicYearId, out var year))
        {
            return ApiProblems.NotFound();
        }

        if (year.Status == AcademicYearStatus.Closed)
        {
            return ApiProblems.Conflict(ProblemCodes.year_closed, "The academic year is closed.");
        }

        var path = unit.Path;
        var targets = await db.Set<Class>()
            .Where(c => c.OrgUnitPath.StartsWith(path) && c.AcademicYearId == request.AcademicYearId)
            .OrderBy(c => c.Code).Take(MaxBulkAssignClasses + 1).ToListAsync(ct);
        if (targets.Count > MaxBulkAssignClasses)
        {
            return ClassValidators.TooMany(nameof(request.OrgUnitId), MaxBulkAssignClasses);
        }
        var known = await lookups.CoursesAsync(unit.OrganizationId, [request.CourseId], ct);
        if (!known.TryGetValue(request.CourseId, out var requested) || requested.Status != Domain.Courses.Course.ActiveStatus)
        {
            return ApiProblems.BadRequest(ProblemCodes.course_not_available, "The course is not available.");
        }

        var unitIds = targets.Select(c => c.OrgUnitId).Distinct().ToList();
        var unitsById = await db.OrgUnits.Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var available = new Dictionary<Guid, Domain.Courses.Course?>();
        int added = 0, skipped = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var cls in targets)
        {
            if (!available.TryGetValue(cls.OrgUnitId, out var course))
            {
                course = await writer.FindAvailableAsync(request.CourseId, unitsById[cls.OrgUnitId], ct);
                available[cls.OrgUnitId] = course;
            }

            // Closed classes are read-only, classes that already have the course keep their setting, and units the
            // course is not allocated at are left alone.
            if (course is null || cls.Status == Class.ClosedStatus || await writer.ExistsAsync(cls.Id, request.CourseId, ct))
            {
                skipped++;
                continue;
            }

            try
            {
                await writer.AddAsync(cls, [(course, request.IsOptional)], ct);
            }
            catch (DbUpdateException ex) when (DbErrors.IsConcurrentKeyConflict(ex))
            {
                // A concurrent add of the same course won the race: nothing of this request is kept.
                return ApiProblems.Conflict(ProblemCodes.class_course_exists, "A concurrent request already added this course.");
            }

            added++;
        }

        await transaction.CommitAsync(ct);
        return Results.Ok(new BulkAssignResultDto(added, skipped));
    }

    private async Task<(ClassCourse? ClassCourse, IResult? Problem)> LoadWritableAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Classes.ManageCourses, id, ct);
        if (problem is not null)
        {
            return (null, problem);
        }

        var cls = await db.Set<Class>().FirstAsync(c => c.Id == classCourse!.ClassId, ct);
        return await classes.ClosedProblemAsync(cls, ct) is { } closed ? (null, closed) : (classCourse, null);
    }
}
