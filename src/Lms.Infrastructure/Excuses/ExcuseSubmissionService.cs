using Lms.Domain.Attendance;
using Lms.Domain.Calendar;
using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Excuses;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.Excuses;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Excuses;

/// <summary>How the caller stands to the student: a linked guardian, the student themself, or neither (a miss is 404).</summary>
public sealed record ExcuseRelation(StudentProfile Student, bool IsGuardian, bool IsSelf);

/// <summary>An Absent or Late record of a student that no pending or approved excuse covers yet.</summary>
public sealed record ExcusableAbsence(Guid SessionId, DateOnly Date, Guid ClassCourseId, string Status);

/// <summary>
/// The read side of submitting an excuse (FR-EXC-02, D61, L13, L21): who the caller is to the student, the client's policy rules, and the absences an excuse may
/// still cover. Everything is read through the caller's own filters (a guardian reaches their child through the link, never through a unit scope), so a student or
/// session the caller cannot see is simply absent.
/// </summary>
public sealed class ExcuseSubmissionService(LmsDbContext db, ISettingsResolver settings, IClientCalendar calendar)
{
    public async Task<ExcuseRelation?> RelationAsync(Guid studentId, Guid callerUserId, CancellationToken ct)
    {
        var student = await db.Set<StudentProfile>().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null)
        {
            return null;
        }

        var isSelf = student.UserId == callerUserId;
        var isGuardian = await (from link in db.Set<StudentGuardian>()
                                join g in db.Set<GuardianProfile>() on link.GuardianId equals g.Id
                                where link.StudentId == studentId && g.UserId == callerUserId
                                select link.Id).AnyAsync(ct);
        return isSelf || isGuardian ? new ExcuseRelation(student, isGuardian, isSelf) : null;
    }

    /// <summary>True when the caller is, right now, the student or a linked guardian of the student (a past link does not count).</summary>
    public async Task<bool> CanActForAsync(Guid studentId, Guid callerUserId, CancellationToken ct) => await RelationAsync(studentId, callerUserId, ct) is not null;

    /// <summary>True when this caller may submit for the student: a guardian always, the student only when the client allows it (<c>Excuses.Submitters</c>).</summary>
    public async Task<bool> CanSubmitAsync(ExcuseRelation relation, CancellationToken ct) =>
        relation.IsGuardian || relation.IsSelf && await SubmittersAsync(relation.Student.OrgUnitPath, ct) == ExcuseSubmitters.GuardiansAndStudents;

    public async Task<string> SubmittersAsync(string unitPath, CancellationToken ct) => (await settings.ResolveAsync(SettingKeys.ExcusesSubmitters, unitPath, ct)).Value;

    public async Task<ExcuseRules> RulesAsync(string unitPath, CancellationToken ct) => new(
        (await settings.ResolveAsync(SettingKeys.ExcusesAllowedReasons, unitPath, ct)).Value.ToHashSet(),
        (await settings.ResolveAsync(SettingKeys.ExcusesAttachmentRequiredReasons, unitPath, ct)).Value.ToHashSet(),
        (await settings.ResolveAsync(SettingKeys.ExcusesMaxDaysAfterAbsence, unitPath, ct)).Value);

    public IClientCalendar Calendar => calendar;

    public Task<DateOnly> TodayAsync(Guid organizationId, CancellationToken ct) => calendar.LocalTodayAsync(organizationId, ct);

    /// <summary>The student's Absent or Late records between two local dates (optionally only some sessions) that no Submitted, Escalated or Approved excuse covers.</summary>
    public async Task<List<ExcusableAbsence>> AbsencesAsync(
        Guid studentId, DateOnly fromDate, DateOnly toDate, IReadOnlyCollection<Guid>? sessionIds, CancellationToken ct)
    {
        var covered = from link in db.Set<AbsenceExcuseSession>()
                      join e in db.Set<AbsenceExcuse>() on link.AbsenceExcuseId equals e.Id
                      where e.StudentProfileId == studentId && (e.Status == ExcuseStatuses.Submitted || e.Status == ExcuseStatuses.Escalated || e.Status == ExcuseStatuses.Approved)
                      select link.AttendanceSessionId;
        var rows = from r in db.Set<AttendanceRecord>()
                   join s in db.Set<AttendanceSession>() on r.AttendanceSessionId equals s.Id
                   where r.StudentProfileId == studentId && (r.Status == Lms.Shared.Attendance.AttendanceStatuses.Absent || r.Status == Lms.Shared.Attendance.AttendanceStatuses.Late)
                         && s.State == AttendanceSession.TakenState && s.Date >= fromDate && s.Date <= toDate && !covered.Contains(s.Id)
                   select new { SessionId = s.Id, s.Date, s.ClassCourseId, r.Status };
        var list = (await rows.OrderBy(a => a.Date).ThenBy(a => a.SessionId).ToListAsync(ct))
            .Select(a => new ExcusableAbsence(a.SessionId, a.Date, a.ClassCourseId, a.Status!)).ToList();
        return sessionIds is null ? list : list.Where(a => sessionIds.Contains(a.SessionId)).ToList();
    }

    /// <summary>Course names of class courses, for a list: the course sits at an ancestor unit above the caller's scope, so it is read by id; only names leave.</summary>
    public Task<Dictionary<Guid, (string Ar, string En)>> CourseNamesAsync(Guid organizationId, IReadOnlyCollection<Guid> classCourseIds, CancellationToken ct) =>
        CourseNamesAsync(db, organizationId, classCourseIds, ct);

    /// <summary>See <see cref="CourseNamesAsync(Guid, IReadOnlyCollection{Guid}, CancellationToken)"/>; shared with the excuse mapper.</summary>
    public static async Task<Dictionary<Guid, (string Ar, string En)>> CourseNamesAsync(
        LmsDbContext db, Guid organizationId, IReadOnlyCollection<Guid> classCourseIds, CancellationToken ct)
    {
        if (classCourseIds.Count == 0)
        {
            return [];
        }

        var courseOf = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).Select(c => new { c.Id, c.CourseId }).ToListAsync(ct);
        var courseIds = courseOf.Select(c => c.CourseId).Distinct().ToList();
        // ISOLATION-BYPASS: the course of a class course is owned by an ancestor unit above the caller's scope; only its names are read, for the courses of class courses the
        // caller already sees. Client matched, deleted rows excluded.
        var names = await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => courseIds.Contains(c.Id) && c.OrganizationId == organizationId && !c.IsDeleted).ToDictionaryAsync(c => c.Id, c => (c.NameAr, c.NameEn), ct);
        return courseOf.ToDictionary(c => c.Id, c => names.GetValueOrDefault(c.CourseId, (string.Empty, string.Empty)));
    }
}
