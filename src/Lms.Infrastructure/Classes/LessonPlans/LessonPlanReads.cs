using Lms.Domain.AcademicYears;
using Lms.Domain.Blueprints;
using Lms.Domain.Classes;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Classes.LessonPlans;

/// <summary>A published blueprint version with its tree, as plain data.</summary>
public sealed record BlueprintTree(Blueprint Blueprint, IReadOnlyList<BlueprintSection> Sections, IReadOnlyList<BlueprintLesson> Lessons);

/// <summary>
/// The reads of the lesson plan module that lie outside the caller's unit scope: a course (and so its blueprint) is owned by
/// a unit that is usually an ancestor of the classes that teach it, and publishing or pushing runs for classes anywhere in
/// the client. Each query is matched to one client and excludes deleted rows by hand; plans are only ever written for class
/// courses of that client.
/// </summary>
public sealed class LessonPlanReads(LmsDbContext db)
{
    /// <summary>The latest published version of the course's blueprint, with its tree; null when the course has none.</summary>
    public async Task<BlueprintTree?> LatestPublishedAsync(Guid organizationId, Guid courseId, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the blueprint is owned by the course's owner unit, above the class units that copy it; client
        // matched, deleted blueprints excluded by hand.
        var blueprint = await db.Set<Blueprint>().IgnoreQueryFilters()
            .Where(b => b.OrganizationId == organizationId && b.CourseId == courseId && !b.IsDeleted && b.Status == Blueprint.PublishedStatus)
            .OrderByDescending(b => b.Version)
            .FirstOrDefaultAsync(ct);
        return blueprint is null ? null : await TreeAsync(blueprint, ct);
    }

    public async Task<BlueprintTree> TreeAsync(Blueprint blueprint, CancellationToken ct)
    {
        // ISOLATION-BYPASS: sections and lessons carry the blueprint's owner unit path; client and blueprint matched by hand.
        var sections = await db.Set<BlueprintSection>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == blueprint.OrganizationId && s.BlueprintId == blueprint.Id)
            .OrderBy(s => s.Order).ToListAsync(ct);
        // ISOLATION-BYPASS: same as above, for the lessons.
        var lessons = await db.Set<BlueprintLesson>().IgnoreQueryFilters()
            .Where(l => l.OrganizationId == blueprint.OrganizationId && l.BlueprintId == blueprint.Id)
            .OrderBy(l => l.Order).ToListAsync(ct);
        return new BlueprintTree(blueprint, sections, lessons);
    }

    /// <summary>The academic year of a class (its start date schedules the lessons; a year may be defined above the caller's scope).</summary>
    public Task<AcademicYear?> YearAsync(Class cls, CancellationToken ct) =>
        // ISOLATION-BYPASS: the year of a class can be defined at an ancestor unit; client matched, deleted years excluded.
        db.Set<AcademicYear>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(y => y.Id == cls.AcademicYearId && y.OrganizationId == cls.OrganizationId && !y.IsDeleted, ct);
}
