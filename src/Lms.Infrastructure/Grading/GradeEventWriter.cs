using Lms.Domain.Classes;
using Lms.Domain.Grading;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Grading;

internal sealed class GradeEventWriter(LmsDbContext db) : IGradeEvents
{
    public async Task<bool> RecordAsync(GradeEventInput input, CancellationToken ct)
    {
        if (input.Revision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Revision starts at 1.");
        }

        var local = db.Set<GradeEvent>().Local.Any(e =>
            e.SourceKind == input.SourceKind && e.SourceId == input.SourceId
            && e.StudentProfileId == input.StudentProfileId && e.Revision == input.Revision);
        if (local || await db.Set<GradeEvent>().AnyAsync(e =>
                e.SourceKind == input.SourceKind && e.SourceId == input.SourceId
                && e.StudentProfileId == input.StudentProfileId && e.Revision == input.Revision, ct))
        {
            return false;
        }

        var classCourse = await db.Set<ClassCourse>().SingleAsync(c => c.Id == input.ClassCourseId, ct);
        db.Add(new GradeEvent(classCourse, input));
        return true;
    }

    public async Task<bool> RecordScoreAsync(
        GradeSourceKind sourceKind, Guid sourceId, Guid classCourseId, Guid studentProfileId, decimal points, decimal maxPoints,
        DateTime scoredAtUtc, CancellationToken ct)
    {
        var stored = await db.Set<GradeEvent>()
            .Where(e => e.SourceKind == sourceKind && e.SourceId == sourceId && e.StudentProfileId == studentProfileId)
            .OrderByDescending(e => e.Revision).Select(e => new { e.Revision, e.Points, e.MaxPoints }).FirstOrDefaultAsync(ct);
        var pending = db.Set<GradeEvent>().Local
            .Where(e => e.SourceKind == sourceKind && e.SourceId == sourceId && e.StudentProfileId == studentProfileId)
            .OrderByDescending(e => e.Revision).Select(e => new { e.Revision, e.Points, e.MaxPoints }).FirstOrDefault();
        var latest = pending ?? stored;
        if (latest is not null && latest.Points == points && latest.MaxPoints == maxPoints)
        {
            return false;
        }

        return await RecordAsync(
            new GradeEventInput(sourceKind, sourceId, classCourseId, studentProfileId, points, maxPoints, scoredAtUtc, (latest?.Revision ?? 0) + 1), ct);
    }
}
