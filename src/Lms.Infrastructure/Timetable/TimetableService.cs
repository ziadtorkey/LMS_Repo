using Lms.Domain.AcademicYears;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Teaching;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Timetable;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Timetable;

/// <summary>Reads and checks the slots of a class's timetable (FR-TT-02, FR-TT-03). Writes stay in the endpoints so the access check comes first.</summary>
public sealed class TimetableService(LmsDbContext db)
{
    public Task<Class?> FindClassAsync(Guid classId, CancellationToken ct) =>
        db.Set<Class>().SingleOrDefaultAsync(c => c.Id == classId, ct);

    /// <summary>A term of the class's academic year (the year may be defined at an ancestor unit above the caller).</summary>
    public async Task<Term?> FindTermAsync(Class @class, Guid termId, CancellationToken ct) =>
        // ISOLATION-BYPASS: the term belongs to the class's academic year, which can be defined at an ancestor unit above the caller's scope;
        // limited to the client, live rows and the year of the class the caller already reaches.
        await db.Set<Term>().IgnoreQueryFilters()
            .SingleOrDefaultAsync(t => !t.IsDeleted && t.OrganizationId == @class.OrganizationId && t.AcademicYearId == @class.AcademicYearId && t.Id == termId, ct);

    public Task<bool> TeachesAsync(Guid classCourseId, Guid userId, CancellationToken ct) =>
        db.Set<ClassCourseTeacher>().AnyAsync(t => t.ClassCourseId == classCourseId && t.UserId == userId, ct);

    public Task<List<TimetableSlot>> SlotsAsync(Guid classId, Guid termId, bool includeDrafts, CancellationToken ct) =>
        db.Set<TimetableSlot>().Where(s => s.ClassId == classId && s.TermId == termId && (includeDrafts || s.State == TimetableSlot.PublishedState))
            .OrderBy(s => s.Day).ThenBy(s => s.PeriodId).ToListAsync(ct);

    /// <summary>Slots of the term in the same client that take the same day and period for the teacher or the class: the clash candidates, in any class or school.</summary>
    public async Task<List<TimetableSlot>> ClashCandidatesAsync(Class @class, Guid termId, TimetableSlot candidate, CancellationToken ct) =>
        // ISOLATION-BYPASS: a teacher can teach in classes of sibling schools, outside the caller's subtree; limited to the client, live rows, one term,
        // one day and period; only names and ids of the clashing slots leave (the builder shows why a save was refused).
        await db.Set<TimetableSlot>().IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.OrganizationId == @class.OrganizationId && s.TermId == termId && s.Day == candidate.Day && s.PeriodId == candidate.PeriodId
                && s.Id != candidate.Id && (s.TeacherUserId == candidate.TeacherUserId || s.ClassId == candidate.ClassId))
            .ToListAsync(ct);

    public static IReadOnlyList<SlotClash> Clashes(TimetableSlot candidate, IEnumerable<TimetableSlot> others) =>
        ConflictChecker.Find(ToPlace(candidate), others.Select(ToPlace));

    public static SlotPlace ToPlace(TimetableSlot s) => new(s.Id, s.ClassId, s.TeacherUserId, s.Day, s.PeriodId);

    public async Task<IReadOnlyList<SlotConflictDto>> DescribeAsync(Guid organizationId, IReadOnlyList<SlotClash> clashes, IReadOnlyCollection<TimetableSlot> slots, CancellationToken ct)
    {
        var bySlot = slots.ToDictionary(s => s.Id);
        var names = await NamesAsync(organizationId, slots, ct);
        return clashes.Where(c => bySlot.ContainsKey(c.SlotId)).Select(c =>
        {
            var slot = bySlot[c.SlotId];
            return new SlotConflictDto(c.Kind, slot.Id, names.CourseAr(slot), names.CourseEn(slot), names.Teacher(slot));
        }).ToList();
    }

    public async Task<IReadOnlyList<SlotDto>> ToDtosAsync(Guid organizationId, IReadOnlyCollection<TimetableSlot> slots, ISet<Guid>? conflicting, CancellationToken ct)
    {
        var names = await NamesAsync(organizationId, slots, ct);
        return slots.Select(s => new SlotDto(s.Id, s.ClassId, s.ClassCourseId, s.TermId, names.CourseAr(s), names.CourseEn(s), s.TeacherUserId, names.Teacher(s),
            s.Day, s.PeriodId, s.RoomName, s.State, s.Version,
            conflicting is not null && conflicting.Contains(s.Id) ? [SlotBadges.Conflict] : [])).ToList();
    }

    private async Task<SlotNames> NamesAsync(Guid organizationId, IReadOnlyCollection<TimetableSlot> slots, CancellationToken ct)
    {
        var courseIds = slots.Select(s => s.ClassCourseId).Distinct().ToList();
        // ISOLATION-BYPASS: the class course of a clashing slot may sit in a sibling school outside the caller scope; only its course id is read, limited to the client and live rows.
        var ccToCourse = await db.Set<ClassCourse>().IgnoreQueryFilters()
            .Where(cc => !cc.IsDeleted && cc.OrganizationId == organizationId && courseIds.Contains(cc.Id))
            .Select(cc => new { cc.Id, cc.CourseId }).ToListAsync(ct);
        var courseKeys = ccToCourse.Select(x => x.CourseId).Distinct().ToList();

        // ISOLATION-BYPASS: courses are owned by ancestor units above the caller's scope; only the names leave, limited to the client and live rows.
        var courses = await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.OrganizationId == organizationId && courseKeys.Contains(c.Id))
            .Select(c => new CourseNames(c.Id, c.NameAr, c.NameEn)).ToListAsync(ct);
        var teacherIds = slots.Select(s => s.TeacherUserId).Distinct().ToList();
        var users = await db.Users.Where(u => teacherIds.Contains(u.Id)).Select(u => new { u.Id, u.DisplayNameAr, u.DisplayNameEn }).ToListAsync(ct);

        return new SlotNames(
            ccToCourse.ToDictionary(x => x.Id, x => courses.FirstOrDefault(c => c.Id == x.CourseId)),
            users.ToDictionary(u => u.Id, u => u.DisplayNameEn is { Length: > 0 } en ? en : u.DisplayNameAr));
    }

    private sealed record CourseNames(Guid Id, string NameAr, string NameEn);

    private sealed record SlotNames(Dictionary<Guid, CourseNames?> Courses, Dictionary<Guid, string> Teachers)
    {
        public string CourseAr(TimetableSlot s) => Courses.TryGetValue(s.ClassCourseId, out var c) && c is not null ? c.NameAr : string.Empty;
        public string CourseEn(TimetableSlot s) => Courses.TryGetValue(s.ClassCourseId, out var c) && c is not null ? c.NameEn : string.Empty;
        public string Teacher(TimetableSlot s) => Teachers.GetValueOrDefault(s.TeacherUserId, string.Empty);
    }
}
