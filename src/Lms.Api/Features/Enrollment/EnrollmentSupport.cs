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
            extensions: rule.FailedTypes.Count == 0
                ? new Dictionary<string, object?> { ["code"] = rule.Code }
                : new Dictionary<string, object?> { ["code"] = rule.Code, ["failedTypes"] = rule.FailedTypes });

    public static IResult Invalid(string field, string key) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [key] },
            extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });
}

/// <summary>
/// The relationship rule (PRD 3.3 step 2, D32) for the enrollment reads: a Teacher sees only the class courses where they
/// teach (a current teacher assignment) or whose class they are homeroom teacher of. Evaluated after the permission, scope
/// and policy checks; roles without a rule are unrestricted inside their unit scope. Task 12 may replace this by the
/// general service; the rule itself comes from <see cref="RelationshipRules"/>.
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

    /// <summary>The class courses the caller teaches or is homeroom teacher for.</summary>
    public IQueryable<ClassCourse> OwnClassCourses()
    {
        var user = context.UserId;
        var today = DateOnly.FromDateTime(clock.UtcNow);
        return db.Set<ClassCourse>().Where(cc =>
            db.Set<Lms.Domain.Teaching.ClassCourseTeacher>().Any(t =>
                t.ClassCourseId == cc.Id && t.UserId == user && (t.To == null || t.To >= today))
            || db.Set<Class>().Any(c => c.Id == cc.ClassId && c.HomeroomTeacherUserId == user));
    }
}
