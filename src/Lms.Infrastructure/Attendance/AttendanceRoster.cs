using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Attendance;

/// <summary>The roster of a session: the students enrolled in the ClassCourse on the session date (FR-ATT-01). It is computed, never stored.</summary>
public sealed class AttendanceRoster(LmsDbContext db)
{
    public Task<List<Guid>> StudentIdsAsync(Guid classCourseId, DateOnly date, CancellationToken ct) =>
        db.Set<CourseEnrollment>()
            .Where(e => e.ClassCourseId == classCourseId && e.From <= date && (e.To == null || e.To >= date))
            .Select(e => e.StudentId).Distinct().ToListAsync(ct);

    /// <summary>The roster size per (class course, date).</summary>
    public async Task<Dictionary<(Guid ClassCourseId, DateOnly Date), int>> CountsAsync(IReadOnlyCollection<(Guid ClassCourseId, DateOnly Date)> keys, CancellationToken ct)
    {
        var result = new Dictionary<(Guid, DateOnly), int>();
        foreach (var group in keys.GroupBy(k => k.ClassCourseId))
        {
            var rows = await db.Set<CourseEnrollment>().Where(e => e.ClassCourseId == group.Key)
                .Select(e => new { e.StudentId, e.From, e.To }).ToListAsync(ct);
            foreach (var key in group.Distinct())
            {
                result[key] = rows.Where(r => r.From <= key.Date && (r.To == null || r.To >= key.Date)).Select(r => r.StudentId).Distinct().Count();
            }
        }

        return result;
    }
}
