using Lms.Api.Features.Classes;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.Teaching;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Teaching;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Teaching;

/// <summary>Teacher course eligibility (FR-TCH-01): who may teach which course at a unit and below.</summary>
public sealed class EligibilityHandlers(
    LmsDbContext db, ICurrentContext context, ClassCourseWriter courses, ClassLookups lookups, TeachingLookups teaching,
    IRelationshipScope relationships)
{
    private const int MaxPairs = 200;

    public async Task<IResult> GrantAsync(GrantEligibilityRequest request, HttpContext http, CancellationToken ct)
    {
        if (request?.UserIds is not { Count: > 0 } users || request.CourseIds is not { Count: > 0 } courseIds
            || users.Distinct().Count() != users.Count || courseIds.Distinct().Count() != courseIds.Count)
        {
            return ClassValidators.Invalid(nameof(GrantEligibilityRequest.UserIds), "pairs_invalid");
        }

        if ((long)users.Count * courseIds.Count > MaxPairs)
        {
            return ClassValidators.TooMany(nameof(request.UserIds), MaxPairs);
        }

        var access = await http.CheckUnitAsync(Permissions.TeacherAssignments.Manage, request.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        // D42: a Supervisor decides who teaches only the courses they supervise; any other course answers 404, whichever it is.
        var filter = await relationships.ForAsync(Permissions.TeacherAssignments.Manage, ct);
        if (courseIds.Any(id => !filter.AllowsCourse(id)))
        {
            return ApiProblems.NotFound();
        }

        foreach (var userId in users)
        {
            if (!await teaching.IsTeacherNearAsync(unit.OrganizationId, userId, unit.Path, ct))
            {
                return ApiProblems.BadRequest(ProblemCodes.teacher_not_in_unit, "A teacher has no Teacher role at this unit.");
            }
        }

        var found = new List<Domain.Courses.Course>();
        foreach (var courseId in courseIds)
        {
            if (await courses.FindAvailableAsync(courseId, unit, ct) is not { } course)
            {
                return ApiProblems.BadRequest(ProblemCodes.course_not_available, "A course is not available at this unit.");
            }

            found.Add(course);
        }

        var existing = (await db.Set<TeacherCourseEligibility>()
                .Where(e => e.OrgUnitId == unit.Id && users.Contains(e.UserId) && courseIds.Contains(e.CourseId))
                .Select(e => new { e.UserId, e.CourseId }).ToListAsync(ct))
            .Select(e => (e.UserId, e.CourseId)).ToHashSet();
        var created = (from userId in users
                       from course in found
                       where !existing.Contains((userId, course.Id))
                       select new TeacherCourseEligibility(unit, userId, course)).ToList();
        db.AddRange(created);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsConcurrentKeyConflict(ex))
        {
            // A concurrent grant of the same pair won the race: the pair is there, which is what the caller asked for.
            db.ChangeTracker.Clear();
            var winners = await db.Set<TeacherCourseEligibility>()
                .Where(e => e.OrgUnitId == unit.Id && users.Contains(e.UserId) && courseIds.Contains(e.CourseId)).ToListAsync(ct);
            return Results.Created(Routes.TeacherEligibility, await ToDtosAsync(winners, unit.OrganizationId, ct));
        }

        return Results.Created(Routes.TeacherEligibility, await ToDtosAsync(created, unit.OrganizationId, ct));
    }

    public async Task<IResult> ListAsync(EligibilityQuery query, HttpContext http, CancellationToken ct)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > Paging.MaxPageSize)
        {
            return ClassValidators.Invalid(query.Page < 1 ? nameof(query.Page) : nameof(query.PageSize), "paging_invalid");
        }

        var unitId = query.OrgUnitId;
        if (unitId is null && context.ScopePath is { } scope)
        {
            unitId = await db.OrgUnits.Where(u => u.Path == scope).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        }

        if (unitId is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.TeacherAssignments.View, unitId.Value, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var unit = access.Unit!;
        var path = unit.Path;
        var rows = query.IncludeAncestors
            ? teaching.EligibilitiesAppliedAt(unit.OrganizationId, path)
            : db.Set<TeacherCourseEligibility>().Where(e => e.OrgUnitPath.StartsWith(path));
        if (query.UserId is { } userId)
        {
            rows = rows.Where(e => e.UserId == userId);
        }

        if (query.CourseId is { } courseId)
        {
            rows = rows.Where(e => e.CourseId == courseId);
        }

        // A teacher who may only view reaches their own eligibility, nobody else's.
        if (!(await relationships.ForAsync(Permissions.TeacherAssignments.View, ct)).IsUnrestricted)
        {
            var self = context.UserId;
            rows = rows.Where(e => e.UserId == self);
        }

        var total = await rows.CountAsync(ct);
        var page = await rows.OrderBy(e => e.OrgUnitPath).ThenBy(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Results.Ok(new PagedResult<TeacherEligibilityDto>(await ToDtosAsync(page, unit.OrganizationId, ct), total, query.Page, query.PageSize));
    }

    public async Task<IResult> DeleteAsync(Guid id, HttpContext http, CancellationToken ct)
    {
        var row = await db.Set<TeacherCourseEligibility>().FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.TeacherAssignments.Manage, row.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (!(await relationships.ForAsync(Permissions.TeacherAssignments.Manage, ct)).AllowsCourse(row.CourseId))
        {
            return ApiProblems.NotFound();
        }

        // Teacher assignments already made keep running: eligibility is checked only when assigning.
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private async Task<IReadOnlyList<TeacherEligibilityDto>> ToDtosAsync(
        IReadOnlyList<TeacherCourseEligibility> rows, Guid organizationId, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var courses = await lookups.CoursesAsync(organizationId, rows.Select(r => r.CourseId).Distinct().ToList(), ct);
        var units = await lookups.UnitNamesAsync(organizationId, rows.Select(r => r.OrgUnitId).Distinct().ToList(), ct);
        return rows.Select(r =>
        {
            users.TryGetValue(r.UserId, out var user);
            courses.TryGetValue(r.CourseId, out var course);
            units.TryGetValue(r.OrgUnitId, out var unit);
            return new TeacherEligibilityDto(
                r.Id, r.UserId, user?.DisplayNameAr ?? string.Empty, user?.DisplayNameEn ?? string.Empty, r.CourseId,
                course?.Code ?? string.Empty, course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty,
                r.OrgUnitId, unit.NameAr ?? string.Empty, unit.NameEn ?? string.Empty);
        }).ToList();
    }
}
