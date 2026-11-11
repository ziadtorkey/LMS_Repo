using Lms.Api.Infrastructure;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Groups;

/// <summary>Groups of a class course (FR-GRP-01..03, D6): create, list, edit and delete. Members are in <see cref="GroupMemberEndpoints"/>.</summary>
public static class GroupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(Routes.ClassCourseGroupsPattern, CreateAsync).RequireAuthorization().RequirePermission(Permissions.Groups.Manage);
        app.MapGet(Routes.ClassCourseGroupsPattern, ListAsync).RequireAuthorization().RequirePermission(Permissions.Groups.View);
        app.MapPut(Routes.GroupPattern, UpdateAsync).RequireAuthorization().RequirePermission(Permissions.Groups.Manage);
        app.MapDelete(Routes.GroupPattern, DeleteAsync).RequireAuthorization().RequirePermission(Permissions.Groups.Manage);
        GroupMemberEndpoints.Map(app);
    }

    /// <summary>The permission at the class course's unit, then the relationship rule: a teacher reaches only their own class courses (404).</summary>
    internal static async Task<IResult?> AccessAsync(
        ClassCourse classCourse, string permission, HttpContext http, EnrollmentRelationship relationship, CancellationToken ct)
    {
        var access = await http.CheckUnitAsync(permission, classCourse.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        return await relationship.RestrictsAsync(permission, ct)
               && !await relationship.TeachingClassCourses().AnyAsync(c => c.Id == classCourse.Id, ct)
            ? ApiProblems.NotFound()
            : null;
    }

    private static async Task<IResult> CreateAsync(
        Guid id, SaveGroupRequest request, HttpContext http, LmsDbContext db, EnrollmentRelationship relationship, CancellationToken ct)
    {
        var classCourse = await GroupSupport.ClassCourseAsync(db, id, ct);
        if (classCourse is null)
        {
            return ApiProblems.NotFound();
        }

        if (await AccessAsync(classCourse, Permissions.Groups.Manage, http, relationship, ct) is { } denied)
        {
            return denied;
        }

        if (await GroupSupport.ValidateAsync(request, classCourse.OrganizationId, db, ct) is { } invalid)
        {
            return invalid;
        }

        var group = new StudentGroup(classCourse, request.NameAr, request.NameEn, request.MatchType, request.MatchValue);
        db.Set<StudentGroup>().Add(group);
        await db.SaveChangesAsync(ct);
        return Results.Created(Routes.Group(group.Id), GroupSupport.ToDto(group, 0));
    }

    private static async Task<IResult> ListAsync(
        Guid id, HttpContext http, LmsDbContext db, EnrollmentRelationship relationship, CancellationToken ct)
    {
        var classCourse = await GroupSupport.ClassCourseAsync(db, id, ct);
        if (classCourse is null)
        {
            return ApiProblems.NotFound();
        }

        if (await AccessAsync(classCourse, Permissions.Groups.View, http, relationship, ct) is { } denied)
        {
            return denied;
        }

        var groups = await db.Set<StudentGroup>().Where(g => g.ClassCourseId == id).OrderBy(g => g.NameEn).ThenBy(g => g.Id).ToListAsync(ct);
        var counts = await GroupSupport.MemberCountsAsync(db, groups.Select(g => g.Id).ToList(), ct);
        return Results.Ok(groups.Select(g => GroupSupport.ToDto(g, counts.GetValueOrDefault(g.Id))).ToList());
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, SaveGroupRequest request, HttpContext http, LmsDbContext db, EnrollmentRelationship relationship, CancellationToken ct)
    {
        var group = await db.Set<StudentGroup>().FirstOrDefaultAsync(g => g.Id == id, ct);
        var classCourse = group is null ? null : await GroupSupport.ClassCourseAsync(db, group.ClassCourseId, ct);
        if (group is null || classCourse is null)
        {
            return ApiProblems.NotFound();
        }

        if (await AccessAsync(classCourse, Permissions.Groups.Manage, http, relationship, ct) is { } denied)
        {
            return denied;
        }

        if (await GroupSupport.ValidateAsync(request, classCourse.OrganizationId, db, ct) is { } invalid)
        {
            return invalid;
        }

        group.Update(request.NameAr, request.NameEn, request.MatchType, request.MatchValue);
        await db.SaveChangesAsync(ct);
        var counts = await GroupSupport.MemberCountsAsync(db, [group.Id], ct);
        return Results.Ok(GroupSupport.ToDto(group, counts.GetValueOrDefault(group.Id)));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, HttpContext http, LmsDbContext db, EnrollmentRelationship relationship, CancellationToken ct)
    {
        var group = await db.Set<StudentGroup>().FirstOrDefaultAsync(g => g.Id == id, ct);
        var classCourse = group is null ? null : await GroupSupport.ClassCourseAsync(db, group.ClassCourseId, ct);
        if (group is null || classCourse is null)
        {
            return ApiProblems.NotFound();
        }

        if (await AccessAsync(classCourse, Permissions.Groups.Manage, http, relationship, ct) is { } denied)
        {
            return denied;
        }

        // Soft delete: the members go with the group, which frees their one-group-per-class-course slot.
        db.Set<GroupMember>().RemoveRange(await db.Set<GroupMember>().Where(m => m.GroupId == id).ToListAsync(ct));
        db.Set<StudentGroup>().Remove(group);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
