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
}
