using Lms.Domain.Classes;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Timetable;

/// <summary>
/// Copies a class's timetable from one term to another as drafts (FR-TT-04). It only ever reads and writes the slots of this class, so another class's
/// or school's timetable is never an input. A source slot is skipped, and listed with the reason, when its class course is gone, its teacher is no longer
/// assigned, its period is no longer one of the unit's, or the target term already holds a slot of the class at that day and period.
/// </summary>
public sealed class CopyTermService(LmsDbContext db, TimetableService timetable)
{
    public async Task<CopyTermResultDto> CopyAsync(Class @class, Guid sourceTermId, Guid targetTermId, IReadOnlySet<Guid> periodIds, CancellationToken ct)
    {
        var source = await db.Set<TimetableSlot>().Where(s => s.ClassId == @class.Id && s.TermId == sourceTermId)
            .OrderBy(s => s.Day).ThenBy(s => s.PeriodId).ToListAsync(ct);
        var taken = (await db.Set<TimetableSlot>().Where(s => s.ClassId == @class.Id && s.TermId == targetTermId)
            .Select(s => new { s.Day, s.PeriodId }).ToListAsync(ct)).Select(s => (s.Day, s.PeriodId)).ToHashSet();
        var liveCourses = (await db.Set<ClassCourse>().Where(cc => cc.ClassId == @class.Id).Select(cc => cc.Id).ToListAsync(ct)).ToHashSet();
        var teachers = (await db.Set<ClassCourseTeacher>().Where(t => liveCourses.Contains(t.ClassCourseId))
            .Select(t => new { t.ClassCourseId, t.UserId }).ToListAsync(ct)).Select(t => (t.ClassCourseId, t.UserId)).ToHashSet();

        var copied = 0;
        var skipped = new List<(TimetableSlot Slot, string Reason)>();
        foreach (var slot in source)
        {
            var reason = !liveCourses.Contains(slot.ClassCourseId) ? CopySkipReasons.ClassCourseMissing
                : !teachers.Contains((slot.ClassCourseId, slot.TeacherUserId)) ? CopySkipReasons.TeacherMissing
                : !periodIds.Contains(slot.PeriodId) ? CopySkipReasons.PeriodMissing
                : !taken.Add((slot.Day, slot.PeriodId)) ? CopySkipReasons.SlotExists
                : null;
            if (reason is not null)
            {
                skipped.Add((slot, reason));
                continue;
            }

            db.Set<TimetableSlot>().Add(new TimetableSlot(@class, slot.ClassCourseId, targetTermId, slot.TeacherUserId, slot.Day, slot.PeriodId, slot.RoomName));
            copied++;
        }

        await db.SaveChangesAsync(ct);
        var names = await timetable.ToDtosAsync(@class.OrganizationId, skipped.Select(s => s.Slot).ToList(), null, ct);
        return new CopyTermResultDto(copied,
            skipped.Select((s, i) => new CopySkippedDto(s.Slot.Id, s.Reason, names[i].CourseNameAr, names[i].CourseNameEn)).ToList());
    }
}
