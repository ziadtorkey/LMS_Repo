using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment;

/// <summary>Reads that turn enrollment and placement rows into DTOs (names of students, courses, classes and groups).</summary>
public sealed class EnrollmentQueries(LmsDbContext db)
{
    public async Task<IReadOnlyList<EnrollmentDto>> ToDtosAsync(IReadOnlyList<CourseEnrollment> enrollments, CancellationToken ct)
    {
        if (enrollments.Count == 0)
        {
            return [];
        }

        var studentIds = enrollments.Select(e => e.StudentId).Distinct().ToList();
        var classCourseIds = enrollments.Select(e => e.ClassCourseId).Distinct().ToList();
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();

        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var classCourses = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var classIds = classCourses.Values.Select(c => c.ClassId).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var courses = await CoursesAsync(classCourses.Values.Select(c => c.CourseId).Distinct().ToList(), classes.Values.FirstOrDefault()?.OrganizationId, ct);
        var members = await db.Set<GroupMember>().Where(m => enrollmentIds.Contains(m.EnrollmentId)).ToListAsync(ct);
        var groupIds = members.Select(m => m.GroupId).Distinct().ToList();
        var groups = await db.Set<StudentGroup>().Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        var result = new List<EnrollmentDto>(enrollments.Count);
        foreach (var e in enrollments)
        {
            if (!students.TryGetValue(e.StudentId, out var student) || !classCourses.TryGetValue(e.ClassCourseId, out var cc)
                || !classes.TryGetValue(cc.ClassId, out var cls))
            {
                continue; // a row whose parents are not visible is not shown
            }

            courses.TryGetValue(cc.CourseId, out var course);
            var member = members.FirstOrDefault(m => m.EnrollmentId == e.Id);
            groups.TryGetValue(member?.GroupId ?? Guid.Empty, out var group);
            result.Add(new EnrollmentDto(
                e.Id, e.ClassCourseId, e.StudentId, student.StudentCode, student.NameAr, student.NameEn,
                course?.Code ?? string.Empty, course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty,
                cls.NameAr, cls.NameEn, e.Source, e.Status, e.From, e.To, e.DropReason,
                group?.Id, group?.NameAr, group?.NameEn));
        }

        return result;
    }

    /// <summary>Course names for the class courses. The course is owned by a unit that may sit above the caller's scope.</summary>
    private async Task<Dictionary<Guid, Course>> CoursesAsync(IReadOnlyCollection<Guid> ids, Guid? organizationId, CancellationToken ct)
    {
        if (ids.Count == 0 || organizationId is null)
        {
            return [];
        }

        // ISOLATION-BYPASS: the course of a class course is owned by an ancestor unit above the caller's scope; only its
        // names are shown, for class courses the caller can already see. Client matched, deleted courses excluded by hand.
        return await db.Set<Course>().IgnoreQueryFilters()
            .Where(c => ids.Contains(c.Id) && c.OrganizationId == organizationId && !c.IsDeleted)
            .ToDictionaryAsync(c => c.Id, ct);
    }

    public async Task<IReadOnlyList<StudentTransferDto>> TransferDtosAsync(IReadOnlyList<StudentTransfer> transfers, CancellationToken ct)
    {
        if (transfers.Count == 0)
        {
            return [];
        }

        var classIds = transfers.SelectMany(t => new[] { t.FromClassId, t.ToClassId }).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var userIds = transfers.Where(t => t.CreatedBy != null).Select(t => t.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return transfers.Select(t =>
        {
            classes.TryGetValue(t.FromClassId, out var from);
            classes.TryGetValue(t.ToClassId, out var to);
            var by = t.CreatedBy is { } id && users.TryGetValue(id, out var user) ? user : null;
            return new StudentTransferDto(
                t.Id, t.StudentId, from?.NameAr ?? string.Empty, from?.NameEn ?? string.Empty, to?.NameAr ?? string.Empty,
                to?.NameEn ?? string.Empty, t.EffectiveOn, t.Reason, by?.DisplayNameAr, by?.DisplayNameEn, t.CreatedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<ClassStudentDto>> ClassStudentsAsync(Guid classId, CancellationToken ct)
    {
        var rows = await (
                from p in db.Set<ClassStudent>()
                join s in db.Set<StudentProfile>() on p.StudentId equals s.Id
                where p.ClassId == classId
                orderby p.Status, s.NameEn, p.Id
                select new { p, s })
            .ToListAsync(ct);
        return rows.Select(r => new ClassStudentDto(
            r.p.Id, r.s.Id, r.s.StudentCode, r.s.NameAr, r.s.NameEn, r.s.Gender, r.p.From, r.p.To, r.p.Status)).ToList();
    }
}
