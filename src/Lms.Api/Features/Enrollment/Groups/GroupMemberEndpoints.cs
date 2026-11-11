using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Groups;

/// <summary>
/// Members of a group (D6): only students actively enrolled in the class course, and in at most one group of it. Adding is
/// all or nothing; the database enforces the one-group rule too, so two concurrent requests cannot both succeed.
/// </summary>
public static class GroupMemberEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.GroupMembersPattern, AddAsync).RequireAuthorization().RequirePermission(Permissions.Groups.Manage);
        app.MapPost(Routes.GroupMembersRemovePattern, RemoveAsync).RequireAuthorization().RequirePermission(Permissions.Groups.Manage);
        app.MapGet(Routes.GroupMembersPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Groups.View);
        app.MapGet(Routes.GroupSuggestionsPattern, SuggestAsync).RequireAuthorization().RequirePermission(Permissions.Groups.View);
    }

    private static async Task<(StudentGroup? Group, ClassCourse? ClassCourse, IResult? Problem)> LoadAsync(
        Guid id, string permission, HttpContext http, LmsDbContext db, IRelationshipScope scope, CancellationToken ct)
    {
        var studentGroup = await db.Set<StudentGroup>().FirstOrDefaultAsync(g => g.Id == id, ct);
        var classCourse = studentGroup is null ? null : await GroupSupport.ClassCourseAsync(db, studentGroup.ClassCourseId, ct);
        if (studentGroup is null || classCourse is null)
        {
            return (null, null, ApiProblems.NotFound());
        }

        return (studentGroup, classCourse, await GroupEndpoints.AccessAsync(classCourse, permission, http, scope, ct));
    }

    private static IResult? CheckIds(GroupMembersRequest request, out List<Guid> ids)
    {
        ids = request.StudentIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return EnrollmentProblems.Invalid(nameof(request.StudentIds), "students_required");
        }

        return ids.Count > GroupSupport.MaxMembersPerRequest ? ApiProblems.BadRequest(ProblemCodes.too_many_items) : null;
    }

    private static async Task<IResult> AddAsync(
        Guid id, GroupMembersRequest request, HttpContext http, LmsDbContext db, IRelationshipScope scope, CancellationToken ct)
    {
        var (studentGroup, classCourse, problem) = await LoadAsync(id, Permissions.Groups.Manage, http, db, scope, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (CheckIds(request, out var ids) is { } invalid)
        {
            return invalid;
        }

        var enrollments = await db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == classCourse!.Id && e.Status == CourseEnrollment.ActiveStatus && ids.Contains(e.StudentId))
            .ToListAsync(ct);
        if (enrollments.Select(e => e.StudentId).Distinct().Count() != ids.Count)
        {
            return ApiProblems.BadRequest(ProblemCodes.not_enrolled);
        }

        var existing = await db.Set<GroupMember>().Where(m => m.ClassCourseId == classCourse!.Id && ids.Contains(m.StudentId)).ToListAsync(ct);
        if (existing.Any(m => m.GroupId != studentGroup!.Id))
        {
            return ApiProblems.Conflict(ProblemCodes.already_in_group);
        }

        var inThisGroup = existing.Select(m => m.StudentId).ToHashSet();
        var added = enrollments.Where(e => !inThisGroup.Contains(e.StudentId)).Select(e => new GroupMember(studentGroup!, e)).ToList();
        db.Set<GroupMember>().AddRange(added);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase) == true)
        {
            // A concurrent request put one of the students into another group of this class course first.
            return ApiProblems.Conflict(ProblemCodes.already_in_group);
        }

        return Results.Ok(new { added = added.Count });
    }

    private static async Task<IResult> RemoveAsync(
        Guid id, GroupMembersRequest request, HttpContext http, LmsDbContext db, IRelationshipScope scope, CancellationToken ct)
    {
        var (studentGroup, _, problem) = await LoadAsync(id, Permissions.Groups.Manage, http, db, scope, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (CheckIds(request, out var ids) is { } invalid)
        {
            return invalid;
        }

        var members = await db.Set<GroupMember>().Where(m => m.GroupId == studentGroup!.Id && ids.Contains(m.StudentId)).ToListAsync(ct);
        db.Set<GroupMember>().RemoveRange(members); // soft delete: frees the student for another group
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAsync(
        Guid id, HttpContext http, LmsDbContext db, IRelationshipScope scope, CancellationToken ct)
    {
        var (studentGroup, _, problem) = await LoadAsync(id, Permissions.Groups.View, http, db, scope, ct);
        if (problem is not null)
        {
            return problem;
        }

        var rows = await (
                from m in db.Set<GroupMember>()
                join s in db.Set<StudentProfile>() on m.StudentId equals s.Id
                where m.GroupId == studentGroup!.Id
                orderby s.NameEn, s.Id
                select new GroupMemberDto(s.Id, s.StudentCode, s.NameAr, s.NameEn))
            .ToListAsync(ct);
        return Results.Ok(rows);
    }

    /// <summary>Enrolled students in no group of the class course whose gender, religion or level matches the group's rule (FR-GRP-01).</summary>
    private static async Task<IResult> SuggestAsync(
        Guid id, HttpContext http, LmsDbContext db, IRelationshipScope scope, CancellationToken ct)
    {
        var (studentGroup, classCourse, problem) = await LoadAsync(id, Permissions.Groups.View, http, db, scope, ct);
        if (problem is not null)
        {
            return problem;
        }

        var grouped = db.Set<GroupMember>().Where(m => m.ClassCourseId == classCourse!.Id).Select(m => m.StudentId);
        var candidates = await (
                from e in db.Set<CourseEnrollment>()
                join s in db.Set<StudentProfile>() on e.StudentId equals s.Id
                where e.ClassCourseId == classCourse!.Id && e.Status == CourseEnrollment.ActiveStatus && !grouped.Contains(s.Id)
                orderby s.NameEn, s.Id
                select s)
            .ToListAsync(ct);

        IEnumerable<StudentProfile> matching = candidates;
        if (studentGroup!.MatchType is { } type && studentGroup.MatchValue is { } value)
        {
            matching = type switch
            {
                CourseRestrictionTypes.Religion => candidates.Where(s => string.Equals(s.Religion, value, StringComparison.OrdinalIgnoreCase)),
                CourseRestrictionTypes.Gender => candidates.Where(s => string.Equals(s.Gender, value, StringComparison.OrdinalIgnoreCase)),
                CourseRestrictionTypes.LevelType => await LevelMatchesAsync(classCourse!, value, db, ct) ? candidates : [],
                _ => [],
            };
        }

        return Results.Ok(matching.Select(s => new GroupMemberDto(s.Id, s.StudentCode, s.NameAr, s.NameEn)).ToList());
    }

    /// <summary>The level type is that of the class's unit, so it matches all of the class's students or none.</summary>
    private static async Task<bool> LevelMatchesAsync(ClassCourse classCourse, string value, LmsDbContext db, CancellationToken ct)
    {
        var unit = await db.Set<OrgUnit>().FirstOrDefaultAsync(u => u.Id == classCourse.OrgUnitId, ct);
        return unit is not null && string.Equals(unit.LevelTypeId.ToString(), value, StringComparison.OrdinalIgnoreCase);
    }
}
