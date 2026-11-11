using Lms.Domain.Abstractions;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment;

/// <summary>Answers of the enrollment endpoints for the rule failures of the engine.</summary>
internal static class EnrollmentProblems
{
    public static IResult From(EnrollmentRuleException rule) =>
        Results.Problem(
            statusCode: rule.StatusCode,
            title: rule.StatusCode == StatusCodes.Status400BadRequest ? "Bad request" : "Conflict",
            extensions: Extensions(rule));

    private static Dictionary<string, object?> Extensions(EnrollmentRuleException rule)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = rule.Code };
        if (rule.FailedTypes.Count > 0)
        {
            extensions["failedTypes"] = rule.FailedTypes;
        }

        if (rule.Reason is not null)
        {
            extensions["reason"] = rule.Reason;
        }

        return extensions;
    }

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
}

/// <summary>
/// The relationship rule (PRD 3.3 step 2, D32, D42) for the enrollment reads. Enrollments are teaching-only: a Teacher
/// sees the enrollments of the class courses where they hold a current teacher assignment, nothing else. A homeroom
/// teacher additionally sees their class and its students (Classes.View, Students.View) but not the enrollments of its
/// other courses. Evaluated after the permission, scope and policy checks; roles without a rule are unrestricted inside
/// their unit scope. The rule itself comes from <see cref="RelationshipRules"/>.
/// </summary>
public sealed class EnrollmentRelationship(LmsDbContext db, ICurrentContext context, IClock clock)
{
    /// <summary>True when the active role's permission is restricted to the caller's own class courses.</summary>
    public async Task<bool> RestrictsAsync(string permission, CancellationToken ct)
    {
        if (context.IsPlatformAdmin || context.AssignmentId is not { } assignmentId)
        {
            return false;
        }

        var roleKey = await (
                from a in db.RoleAssignments
                join r in db.Roles on a.RoleId equals r.Id
                where a.Id == assignmentId
                select r.BuiltInKey)
            .FirstOrDefaultAsync(ct);
        return roleKey is not null && RelationshipRules.For(roleKey, permission) == Relationships.OwnClassCourses;
    }

    /// <summary>The class courses the caller teaches now (a teacher assignment that has started and not ended).</summary>
    public IQueryable<ClassCourse> TeachingClassCourses()
    {
        var user = context.UserId;
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return db.Set<ClassCourse>().Where(cc =>
            db.Set<Lms.Domain.Teaching.ClassCourseTeacher>().Any(t =>
                t.ClassCourseId == cc.Id && t.UserId == user && t.From <= today && (t.To == null || t.To >= today)));
    }

    /// <summary>True when the caller teaches a course of the class or is its homeroom teacher (class-level reads).</summary>
    public async Task<bool> IsTeachingOrHomeroomAsync(Guid classId, CancellationToken ct) =>
        await TeachingClassCourses().AnyAsync(cc => cc.ClassId == classId, ct)
        || await db.Set<Class>().AnyAsync(c => c.Id == classId && c.HomeroomTeacherUserId == context.UserId, ct);
}
