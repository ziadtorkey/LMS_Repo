using Lms.Api.Infrastructure;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Courses;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Courses;

/// <summary>PUT /api/courses/{id}/restrictions: replace-all (FR-CRS-03). Existing enrollments are never changed.</summary>
internal static class RestrictionHandlers
{
    /// <summary>How many active enrollments of the course no longer pass the new restrictions (for the UI warning).</summary>
    public const string AffectedHeader = "X-Lms-Affected-Enrollments";

    public static async Task<IResult> SetAsync(
        Guid id, SetCourseRestrictionsRequest request, HttpContext http, LmsDbContext db, CancellationToken ct)
    {
        var inputs = request.Restrictions;
        if (inputs is null || inputs.Any(i => i is null))
        {
            return ApiProblems.BadRequest(ProblemCodes.validation_failed, "Restrictions is required.");
        }

        var (course, problem) = await http.CheckCourseAsync(Permissions.Courses.Update, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        var normalized = await NormalizeAsync(inputs, course!.OrganizationId, db, ct);
        if (normalized.Problem is not null)
        {
            return normalized.Problem;
        }

        var wanted = normalized.Restrictions!;
        var rows = await db.Set<CourseRestriction>().Where(r => r.CourseId == id).ToListAsync(ct);
        foreach (var row in rows.Where(r => !wanted.ContainsKey(r.Type)))
        {
            db.Set<CourseRestriction>().Remove(row);
        }

        foreach (var (type, values) in wanted)
        {
            var existing = rows.FirstOrDefault(r => r.Type == type);
            if (existing is null)
            {
                db.Set<CourseRestriction>().Add(new CourseRestriction(course, type, values));
            }
            else if (!existing.Values.SequenceEqual(values))
            {
                existing.SetValues(values);
            }
        }

        await db.SaveChangesAsync(ct);
        http.Response.Headers[AffectedHeader] = (await CountAffectedAsync(db, id, wanted, ct)).ToString();
        return Results.Ok(wanted.OrderBy(w => w.Key, StringComparer.Ordinal).Select(w => new CourseRestrictionDto(w.Key, w.Value)).ToList());
    }

    /// <summary>One entry per type, values known and distinct; level types must be live types of the course's client.</summary>
    private static async Task<(Dictionary<string, IReadOnlyList<string>>? Restrictions, IResult? Problem)> NormalizeAsync(
        IReadOnlyList<CourseRestrictionInput> inputs, Guid organizationId, LmsDbContext db, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!CourseRestrictionTypes.All.Contains(input.Type) || result.ContainsKey(input.Type))
            {
                return (null, ApiProblems.BadRequest(ProblemCodes.validation_failed, "Unknown or repeated restriction type."));
            }

            var values = input.Values?.Select(v => v?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).ToList() ?? [];
            if (values.Count == 0 || values.Any(v => v.Length == 0))
            {
                return (null, ApiProblems.BadRequest(ProblemCodes.validation_failed, "A restriction needs at least one value."));
            }

            var known = input.Type switch
            {
                CourseRestrictionTypes.Religion => values.All(v => Religions.All.Contains(v)),
                CourseRestrictionTypes.Gender => values.All(v => Genders.All.Contains(v)),
                _ => true,
            };
            if (!known)
            {
                return (null, ApiProblems.BadRequest(ProblemCodes.validation_failed, $"Unknown {input.Type} value."));
            }

            if (input.Type == CourseRestrictionTypes.LevelType)
            {
                var ids = values.Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty).ToList();
                var live = await db.Set<LevelType>().Where(l => l.OrganizationId == organizationId && ids.Contains(l.Id)).CountAsync(ct);
                if (ids.Contains(Guid.Empty) || live != ids.Count)
                {
                    return (null, ApiProblems.BadRequest(ProblemCodes.invalid_level_type, "A level type is unknown for this client."));
                }

                values = ids.Select(g => g.ToString()).ToList();
            }

            result[input.Type] = values;
        }

        return (result, null);
    }

    private static async Task<int> CountAffectedAsync(
        LmsDbContext db, Guid courseId, IReadOnlyDictionary<string, IReadOnlyList<string>> restrictions, CancellationToken ct)
    {
        if (restrictions.Count == 0)
        {
            return 0;
        }

        var classCourseIds = db.Set<ClassCourse>().Where(cc => cc.CourseId == courseId).Select(cc => cc.Id);
        var active = await db.Set<CourseEnrollment>()
            .Where(e => e.Status == CourseEnrollment.ActiveStatus && classCourseIds.Contains(e.ClassCourseId))
            .Select(e => new { e.StudentId, e.OrgUnitId }).ToListAsync(ct);
        if (active.Count == 0)
        {
            return 0;
        }

        var studentIds = active.Select(a => a.StudentId).Distinct().ToList();
        var unitIds = active.Select(a => a.OrgUnitId).Distinct().ToList();
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => (s.Gender, s.Religion), ct);
        var levelTypes = await db.OrgUnits.Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.LevelTypeId, ct);
        var list = restrictions.Select(r => (r.Key, r.Value)).ToList();
        return active.Count(a =>
            students.TryGetValue(a.StudentId, out var s) && levelTypes.TryGetValue(a.OrgUnitId, out var level)
            && CourseRestrictionEvaluator.Failed(list, s.Gender, s.Religion, level).Count > 0);
    }
}
