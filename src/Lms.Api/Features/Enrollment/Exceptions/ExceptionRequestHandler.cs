using System.Text.Json;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Exceptions;

/// <summary>
/// POST /api/class-courses/{id}/exceptions and POST /api/enrollment-exceptions/{id}/cancel (FR-CRS-03, D40): a Counselor, Manager
/// or Org Admin asks to enroll a student of the class who fails a restriction of the course; the requester can withdraw it.
/// </summary>
internal sealed class ExceptionRequestHandler(
    LmsDbContext db, ICurrentContext context, IClock clock, PlacementGuard guard, ExceptionLookups lookups,
    ExceptionMapper mapper, ExceptionNotifier notifier)
{
    private const int MaxReason = 500;

    public async Task<IResult> RequestAsync(Guid classCourseId, RequestExceptionRequest? body, HttpContext http, CancellationToken ct)
    {
        var reason = body?.Reason?.Trim();
        if (body is null || string.IsNullOrEmpty(reason) || reason.Length > MaxReason)
        {
            return EnrollmentProblems.Invalid(nameof(RequestExceptionRequest.Reason), string.IsNullOrEmpty(reason) ? "required" : "too_long");
        }

        var cc = await db.Set<ClassCourse>().FirstOrDefaultAsync(c => c.Id == classCourseId, ct);
        if (cc is null || context.UserId is not { } userId)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.RequestException, cc.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == body.StudentId, ct);
        var cls = await db.Set<Class>().FirstOrDefaultAsync(c => c.Id == cc.ClassId, ct);
        if (student is null || cls is null)
        {
            return ApiProblems.NotFound();
        }

        try
        {
            await guard.EnsureWritableAsync(cls, ct);
        }
        catch (EnrollmentRuleException rule)
        {
            return EnrollmentProblems.From(rule);
        }

        if (!await db.Set<ClassStudent>().AnyAsync(c => c.ClassId == cls.Id && c.StudentId == student.Id && c.Status == ClassStudent.ActiveStatus, ct))
        {
            return ApiProblems.BadRequest(ProblemCodes.not_in_class);
        }

        var restrictions = await guard.RestrictionsAsync(cls.OrganizationId, [cc.CourseId], ct);
        var failed = CourseRestrictionEvaluator.Failed(
            restrictions.GetValueOrDefault(cc.CourseId) ?? [], student.Gender, student.Religion, access.Unit!.LevelTypeId);
        if (failed.Count == 0)
        {
            // nothing to be excepted from: enroll normally
            return ApiProblems.Conflict(ProblemCodes.validation_failed, "The student passes the course restrictions; enroll normally.");
        }

        if (await db.Set<CourseEnrollment>().AnyAsync(
                e => e.ClassCourseId == cc.Id && e.StudentId == student.Id && e.Status == CourseEnrollment.ActiveStatus, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.already_enrolled);
        }

        if (await db.Set<EnrollmentException>().AnyAsync(e => e.ClassCourseId == cc.Id && e.StudentId == student.Id && e.Status == EnrollmentException.PendingStatus, ct))
        {
            return ApiProblems.Conflict(ProblemCodes.exception_pending);
        }

        var request = new EnrollmentException(cc, student.Id, JsonSerializer.Serialize(failed), reason, userId);
        db.Set<EnrollmentException>().Add(request);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ApiProblems.Conflict(ProblemCodes.exception_pending); // the unique pending key: a concurrent request of the pair won
        }

        var course = (await lookups.CoursesAsync(cls.OrganizationId, [cc.CourseId], ct)).GetValueOrDefault(cc.CourseId);
        if (course is not null)
        {
            await notifier.RequestedAsync(request, student, course, await lookups.SupervisorUserIdsAsync(cls.OrganizationId, cc.CourseId, ct), ct);
        }

        return Results.Created(Routes.EnrollmentException(request.Id), (await mapper.ToDtosAsync([request], ct))[0]);
    }

    public async Task<IResult> CancelAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var request = await db.Set<EnrollmentException>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (request is null || context.UserId != request.RequestedBy)
        {
            return ApiProblems.NotFound(); // only the requester can withdraw a request
        }

        var access = await http.CheckUnitAsync(Permissions.Enrollment.RequestException, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (!request.IsPending)
        {
            return ApiProblems.Conflict(ProblemCodes.exception_closed);
        }

        request.Cancel(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.Ok((await mapper.ToDtosAsync([request], ct))[0]);
    }
}
