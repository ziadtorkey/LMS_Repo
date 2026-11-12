using Lms.Domain.Blueprints;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Blueprints;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Blueprints;

/// <summary>
/// Reads blueprints of a course the caller may see. A course owned above the caller (inherited) keeps its blueprints at
/// the owner unit, outside the caller's subtree, so the reads below look beyond the scope filter. Every query is limited
/// to one client and live rows, and callers only reach it after the course itself was found visible.
/// </summary>
public sealed class BlueprintReader(LmsDbContext db)
{
    public async Task<List<Blueprint>> ListAsync(Guid organizationId, Guid courseId, bool includeDraft, CancellationToken ct) =>
        // ISOLATION-BYPASS: blueprints of an inherited course sit at an ancestor unit above the caller; limited to the
        // client, live rows and a course the caller was already found to see.
        await db.Set<Blueprint>().IgnoreQueryFilters()
            .Where(b => !b.IsDeleted && b.OrganizationId == organizationId && b.CourseId == courseId
                && (includeDraft || b.Status != Blueprint.DraftStatus))
            .OrderByDescending(b => b.Status == Blueprint.DraftStatus).ThenByDescending(b => b.Version).ToListAsync(ct);

    public async Task<Blueprint?> FindAsync(Guid organizationId, Guid courseId, int version, bool includeDraft, CancellationToken ct) =>
        // ISOLATION-BYPASS: see ListAsync.
        await db.Set<Blueprint>().IgnoreQueryFilters()
            .Where(b => !b.IsDeleted && b.OrganizationId == organizationId && b.CourseId == courseId && b.Version == version
                && (includeDraft || b.Status != Blueprint.DraftStatus))
            .FirstOrDefaultAsync(ct);

    public Task<Blueprint?> LatestPublishedAsync(Guid organizationId, Guid courseId, CancellationToken ct) =>
        // ISOLATION-BYPASS: see ListAsync.
        db.Set<Blueprint>().IgnoreQueryFilters()
            .Where(b => !b.IsDeleted && b.OrganizationId == organizationId && b.CourseId == courseId && b.Status == Blueprint.PublishedStatus)
            .OrderByDescending(b => b.Version).FirstOrDefaultAsync(ct);

    /// <summary>Every section and lesson key the course ever used (a client may send these back in a draft).</summary>
    public async Task<(HashSet<Guid> Sections, HashSet<Guid> Lessons)> KeysAsync(Guid organizationId, Guid courseId, CancellationToken ct)
    {
        // ISOLATION-BYPASS: keys of the course's own history across versions, limited to the client and the course;
        // only keys are read.
        var blueprintIds = db.Set<Blueprint>().IgnoreQueryFilters()
            .Where(b => !b.IsDeleted && b.OrganizationId == organizationId && b.CourseId == courseId).Select(b => b.Id);
        // ISOLATION-BYPASS: section keys of the same course history, limited to the client.
        var sections = await db.Set<BlueprintSection>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && blueprintIds.Contains(s.BlueprintId)).Select(s => s.Key).ToListAsync(ct);
        // ISOLATION-BYPASS: lesson keys of the same course history, limited to the client.
        var lessons = await db.Set<BlueprintLesson>().IgnoreQueryFilters()
            .Where(l => l.OrganizationId == organizationId && blueprintIds.Contains(l.BlueprintId)).Select(l => l.Key).ToListAsync(ct);
        return (sections.ToHashSet(), lessons.ToHashSet());
    }

    public async Task<BlueprintDto> ToDtoAsync(Blueprint blueprint, CancellationToken ct)
    {
        var (sections, lessons) = await TreeAsync(blueprint, ct);
        var summary = (await SummariesAsync([blueprint], ct))[0] with { SectionCount = sections.Count, LessonCount = lessons.Count };
        var bySection = lessons.GroupBy(l => l.SectionId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Order).ToList());
        return new BlueprintDto(summary, sections.Select(s => new BlueprintSectionDto(
            s.Key, s.Order, s.TitleAr, s.TitleEn,
            bySection.GetValueOrDefault(s.Id, []).Select(l => new BlueprintLessonDto(
                l.Key, l.Order, l.TitleAr, l.TitleEn, l.ObjectivesAr, l.ObjectivesEn, l.PlannedOffsetDays)).ToList())).ToList());
    }

    public async Task<(List<BlueprintSection> Sections, List<BlueprintLesson> Lessons)> TreeAsync(Blueprint blueprint, CancellationToken ct)
    {
        // ISOLATION-BYPASS: the tree of a blueprint the caller was already found to see; limited to its client.
        var sections = await db.Set<BlueprintSection>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == blueprint.OrganizationId && s.BlueprintId == blueprint.Id).OrderBy(s => s.Order).ToListAsync(ct);
        // ISOLATION-BYPASS: see above.
        var lessons = await db.Set<BlueprintLesson>().IgnoreQueryFilters()
            .Where(l => l.OrganizationId == blueprint.OrganizationId && l.BlueprintId == blueprint.Id).ToListAsync(ct);
        return (sections, lessons);
    }

    public async Task<IReadOnlyList<BlueprintSummaryDto>> SummariesAsync(IReadOnlyList<Blueprint> blueprints, CancellationToken ct)
    {
        if (blueprints.Count == 0)
        {
            return [];
        }

        var organizationId = blueprints[0].OrganizationId;
        var ids = blueprints.Select(b => b.Id).ToList();
        // ISOLATION-BYPASS: counts of blueprints the caller was already found to see; limited to the client.
        var sectionCounts = await db.Set<BlueprintSection>().IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId && ids.Contains(s.BlueprintId))
            .GroupBy(s => s.BlueprintId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        // ISOLATION-BYPASS: see above.
        var lessonCounts = await db.Set<BlueprintLesson>().IgnoreQueryFilters()
            .Where(l => l.OrganizationId == organizationId && ids.Contains(l.BlueprintId))
            .GroupBy(l => l.BlueprintId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var publisherIds = blueprints.Where(b => b.PublishedBy is not null).Select(b => b.PublishedBy!.Value).Distinct().ToList();
        var publishers = await db.Users.Where(u => publisherIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return blueprints.Select(b =>
        {
            var publisher = b.PublishedBy is { } id && publishers.TryGetValue(id, out var user) ? user : null;
            return new BlueprintSummaryDto(
                b.Id, b.CourseId, b.Version, b.Status, b.Revision, b.PublishedAt, publisher?.DisplayNameAr, publisher?.DisplayNameEn,
                b.ApplyMode, sectionCounts.GetValueOrDefault(b.Id), lessonCounts.GetValueOrDefault(b.Id));
        }).ToList();
    }
}
