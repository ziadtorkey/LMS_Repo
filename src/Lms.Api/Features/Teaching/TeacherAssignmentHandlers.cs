using Lms.Api.Features.Classes;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Notifications;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Notifications;
using Lms.Shared.Teaching;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Teaching;

/// <summary>Teachers of a class course with dated history: assign, replace, end, list and bulk assign.</summary>
public sealed class TeacherAssignmentHandlers(
    LmsDbContext db, IClock clock, ClassHandlers classes, ClassLookups classLookups, TeachingLookups teaching,
    ITeacherScheduleConflicts conflicts, INotificationPublisher publisher, ILogger<TeacherAssignmentHandlers> logger)
{
    private const int MaxBulk = 200;

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow);

    public async Task<IResult> ListAsync(Guid classCourseId, bool includeHistory, HttpContext http, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.TeacherAssignments.View, classCourseId, ct);
        if (problem is not null)
        {
            return problem;
        }

        var today = Today;
        var rows = db.Set<ClassCourseTeacher>().Where(t => t.ClassCourseId == classCourse!.Id);
        if (!includeHistory)
        {
            rows = rows.Where(t => t.To == null || t.To >= today);
        }

        return Results.Ok(await ToDtosAsync(await rows.OrderBy(t => t.From).ThenBy(t => t.Id).ToListAsync(ct), ct));
    }

    public async Task<IResult> AssignAsync(Guid classCourseId, AssignTeacherRequest request, HttpContext http, CancellationToken ct)
    {
        var (target, problem) = await LoadWritableAsync(classCourseId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (row, invalid) = await AddAsync(target!, request.UserId, request.Role, request.From ?? DefaultFrom(target!), ct);
        if (invalid is not null)
        {
            return invalid;
        }

        await transaction.CommitAsync(ct);
        await NotifyAsync(target!, row!, ct);
        return Results.Created(Routes.ClassCourseTeachers(classCourseId), (await ToDtosAsync([row!], ct))[0]);
    }

    public async Task<IResult> ReplaceAsync(
        Guid classCourseId, Guid assignmentId, ReplaceTeacherRequest request, HttpContext http, CancellationToken ct)
    {
        var (target, problem) = await LoadWritableAsync(classCourseId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var old = await db.Set<ClassCourseTeacher>().FirstOrDefaultAsync(t => t.Id == assignmentId && t.ClassCourseId == classCourseId, ct);
        if (old is null)
        {
            return ApiProblems.NotFound();
        }

        if (old.To is not null)
        {
            return ApiProblems.Conflict(ProblemCodes.validation_failed, "The assignment has already ended.");
        }

        if (request.EffectiveOn <= old.From)
        {
            return ClassValidators.Invalid(nameof(request.EffectiveOn), "must_be_after_start");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        old.End(request.EffectiveOn.AddDays(-1));
        await db.SaveChangesAsync(ct); // frees the open primary slot before the new row takes it
        var (row, invalid) = await AddAsync(target!, request.NewUserId, old.Role, request.EffectiveOn, ct);
        if (invalid is not null)
        {
            return invalid;
        }

        await transaction.CommitAsync(ct);
        await NotifyAsync(target!, row!, ct);
        return Results.Ok((await ToDtosAsync([row!], ct))[0]);
    }

    public async Task<IResult> EndAsync(Guid classCourseId, Guid assignmentId, EndTeacherRequest request, HttpContext http, CancellationToken ct)
    {
        var (_, problem) = await LoadWritableAsync(classCourseId, http, ct);
        if (problem is not null)
        {
            return problem;
        }

        var row = await db.Set<ClassCourseTeacher>().FirstOrDefaultAsync(t => t.Id == assignmentId && t.ClassCourseId == classCourseId, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        if (row.To is not null)
        {
            return ApiProblems.Conflict(ProblemCodes.validation_failed, "The assignment has already ended.");
        }

        if (request.LastDay < row.From)
        {
            return ClassValidators.Invalid(nameof(request.LastDay), "before_start");
        }

        row.End(request.LastDay);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await ToDtosAsync([row], ct))[0]);
    }

    public async Task<IResult> BulkAsync(BulkAssignTeacherRequest request, HttpContext http, CancellationToken ct)
    {
        if (request?.ClassCourseIds is not { Count: > 0 } ids || ids.Count > MaxBulk || ids.Distinct().Count() != ids.Count)
        {
            return ClassValidators.Invalid(nameof(request.ClassCourseIds), "class_courses_invalid");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var created = new List<(Target Target, ClassCourseTeacher Row)>();
        foreach (var id in ids)
        {
            var (target, problem) = await LoadWritableAsync(id, http, ct);
            if (problem is not null)
            {
                return problem; // nothing is committed: the transaction rolls back on dispose
            }

            var (row, invalid) = await AddAsync(target!, request.UserId, request.Role, DefaultFrom(target!), ct);
            if (invalid is not null)
            {
                return invalid;
            }

            created.Add((target!, row!));
        }

        await transaction.CommitAsync(ct);
        foreach (var (target, row) in created)
        {
            await NotifyAsync(target, row, ct);
        }

        return Results.Ok(await ToDtosAsync(created.Select(c => c.Row).ToList(), ct));
    }

    /// <summary>Today, or the first day of the year when it has not started yet.</summary>
    private DateOnly DefaultFrom(Target target) => target.Year is { } year && Today < year.StartDate ? year.StartDate : Today;

    private sealed record Target(ClassCourse ClassCourse, Class Class, Domain.Organizations.OrgUnit Unit, Domain.AcademicYears.AcademicYear? Year);

    /// <summary>The class course the caller may manage (404 otherwise) in a class and year that are still writable.</summary>
    private async Task<(Target? Target, IResult? Problem)> LoadWritableAsync(Guid classCourseId, HttpContext http, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.TeacherAssignments.Manage, classCourseId, ct);
        if (problem is not null)
        {
            return (null, problem);
        }

        var cls = await db.Set<Class>().FirstAsync(c => c.Id == classCourse!.ClassId, ct);
        if (await classes.ClosedProblemAsync(cls, ct) is { } closed)
        {
            return (null, closed);
        }

        var unit = await db.OrgUnits.FirstAsync(u => u.Id == cls.OrgUnitId, ct);
        var year = (await classLookups.YearsAsync(cls.OrganizationId, [cls.AcademicYearId], ct)).GetValueOrDefault(cls.AcademicYearId);
        return (new Target(classCourse!, cls, unit, year), null);
    }

    /// <summary>The shared rules of every way to give a class course a teacher; the caller owns the transaction.</summary>
    private async Task<(ClassCourseTeacher? Row, IResult? Problem)> AddAsync(
        Target target, Guid userId, string role, DateOnly from, CancellationToken ct)
    {
        if (!TeacherRoles.All.Contains(role))
        {
            return (null, ClassValidators.Invalid(nameof(AssignTeacherRequest.Role), "role_invalid"));
        }

        var cc = target.ClassCourse;
        if (!await classLookups.IsTeacherAtAsync(cc.OrganizationId, userId, target.Unit.Path, ct))
        {
            return (null, ApiProblems.BadRequest(ProblemCodes.teacher_not_in_unit, "The user has no Teacher role at this unit."));
        }

        if (!await teaching.IsEligibleAsync(cc.OrganizationId, userId, cc.CourseId, target.Unit.Path, ct))
        {
            return (null, ApiProblems.BadRequest(ProblemCodes.teacher_not_eligible, "The teacher is not eligible for this course."));
        }

        if (target.Year is { } year && (from < year.StartDate || from > year.EndDate))
        {
            return (null, ClassValidators.Invalid(nameof(AssignTeacherRequest.From), "outside_academic_year"));
        }

        if (role == TeacherRoles.Primary
            && await db.Set<ClassCourseTeacher>().AnyAsync(t => t.ClassCourseId == cc.Id && t.Role == ClassCourseTeacher.PrimaryRole && t.To == null, ct))
        {
            return (null, ApiProblems.Conflict(ProblemCodes.primary_teacher_exists, "The class course already has a primary teacher."));
        }

        _ = await conflicts.FindAsync(cc.Id, userId, from, null, ct); // FR-TCH-03 warning arrives with the timetable (M5)
        var row = new ClassCourseTeacher(cc, userId, role, from);
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return (row, null);
    }

    private async Task NotifyAsync(Target target, ClassCourseTeacher row, CancellationToken ct)
    {
        try
        {
            var course = (await classLookups.CoursesAsync(row.OrganizationId, [target.ClassCourse.CourseId], ct)).GetValueOrDefault(target.ClassCourse.CourseId);
            await publisher.PublishAsync(new NotificationRequest(
                NotificationEvents.TeachingAssigned, row.OrganizationId, [row.UserId],
                new Dictionary<string, string>
                {
                    ["ClassNameAr"] = target.Class.NameAr,
                    ["ClassNameEn"] = target.Class.NameEn,
                    ["CourseNameAr"] = course?.NameAr ?? string.Empty,
                    ["CourseNameEn"] = course?.NameEn ?? string.Empty,
                    ["Role"] = row.Role,
                }), ct);
        }
        catch (Exception ex)
        {
            // The assignment is saved; the teacher simply misses the notification.
            logger.LogWarning(ex, "Notification for teacher assignment {AssignmentId} could not be published", row.Id);
        }
    }

    private async Task<IReadOnlyList<ClassCourseTeacherDto>> ToDtosAsync(IReadOnlyList<ClassCourseTeacher> rows, CancellationToken ct)
    {
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var today = Today;
        return rows.Select(r =>
        {
            users.TryGetValue(r.UserId, out var user);
            return new ClassCourseTeacherDto(
                r.Id, r.UserId, user?.DisplayNameAr ?? string.Empty, user?.DisplayNameEn ?? string.Empty, r.Role, r.From, r.To,
                r.From <= today && (r.To is null || r.To >= today));
        }).ToList();
    }
}
