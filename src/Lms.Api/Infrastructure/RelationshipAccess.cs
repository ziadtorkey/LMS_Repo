using Lms.Domain.Access;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.People;
using Lms.Infrastructure.Access;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Infrastructure;

/// <summary>
/// Access checks for resources reached through a relationship (M3 Task 12): the permission at the resource's unit
/// (<see cref="UnitAccess.CheckUnitAsync"/>) and then the caller's relationship filter. A resource outside the filter
/// is 404, exactly like one outside the caller's unit scope, so its existence is never revealed.
/// </summary>
public static class RelationshipAccess
{
    public static async Task<(ClassCourse? Cc, IResult? Problem)> CheckClassCourseAsync(
        this HttpContext http, string permission, Guid classCourseId, CancellationToken ct)
    {
        var cc = await Db(http).Set<ClassCourse>().FirstOrDefaultAsync(x => x.Id == classCourseId, ct);
        var problem = await CheckAsync(http, permission, cc?.OrgUnitId, f => f.AllowsClassCourse(classCourseId), ct);
        return (problem is null ? cc : null, problem);
    }

    public static async Task<(Class? Class, IResult? Problem)> CheckClassAsync(
        this HttpContext http, string permission, Guid classId, CancellationToken ct)
    {
        var cls = await Db(http).Set<Class>().FirstOrDefaultAsync(x => x.Id == classId, ct);
        var problem = await CheckAsync(http, permission, cls?.OrgUnitId, f => f.AllowsClass(classId), ct);
        return (problem is null ? cls : null, problem);
    }

    /// <summary>The permission is checked at the course's owner unit.</summary>
    public static async Task<(Course? Course, IResult? Problem)> CheckCourseAsync(
        this HttpContext http, string permission, Guid courseId, CancellationToken ct)
    {
        var course = await Db(http).Set<Course>().FirstOrDefaultAsync(x => x.Id == courseId, ct);
        var problem = await CheckAsync(http, permission, course?.OrgUnitId, f => f.AllowsCourse(courseId), ct);
        return (problem is null ? course : null, problem);
    }

    /// <summary>The student's home unit decides the unit check; the filter decides whether the caller reaches them.</summary>
    public static async Task<(StudentProfile? Student, IResult? Problem)> CheckStudentAsync(
        this HttpContext http, string permission, Guid studentId, CancellationToken ct)
    {
        var db = Db(http);
        var filter = await http.RequestServices.GetRequiredService<IRelationshipScope>().ForAsync(permission, ct);
        var student = await filter.Students(db.Set<StudentProfile>().Where(s => s.Id == studentId), db).FirstOrDefaultAsync(ct);
        var problem = await CheckAsync(http, permission, student?.OrgUnitId, _ => true, ct);
        return (problem is null ? student : null, problem);
    }

    private static LmsDbContext Db(HttpContext http) => http.RequestServices.GetRequiredService<LmsDbContext>();

    private static async Task<IResult?> CheckAsync(
        HttpContext http, string permission, Guid? unitId, Func<RelationshipFilter, bool> allows, CancellationToken ct)
    {
        if (unitId is not { } id)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(permission, id, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        var filter = await http.RequestServices.GetRequiredService<IRelationshipScope>().ForAsync(permission, ct);
        return allows(filter) ? null : ApiProblems.NotFound();
    }
}
