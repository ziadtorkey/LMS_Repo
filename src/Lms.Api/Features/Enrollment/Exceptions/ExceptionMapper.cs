using System.Text.Json;
using Lms.Domain.Classes;
using Lms.Domain.Enrollment;
using Lms.Domain.Identity;
using Lms.Domain.People;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Enrollment;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Enrollment.Exceptions;

/// <summary>Builds <see cref="EnrollmentExceptionDto"/>s. Courses are owned above the class, so they come through the lookups.</summary>
internal sealed class ExceptionMapper(LmsDbContext db, ExceptionLookups lookups)
{
    public async Task<IReadOnlyList<EnrollmentExceptionDto>> ToDtosAsync(IReadOnlyList<EnrollmentException> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var organizationId = rows[0].OrganizationId;
        var studentIds = rows.Select(r => r.StudentId).Distinct().ToList();
        var classCourseIds = rows.Select(r => r.ClassCourseId).Distinct().ToList();
        var userIds = rows.Select(r => r.RequestedBy).Concat(rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value)).Distinct().ToList();
        var students = await db.Set<StudentProfile>().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var classCourses = await db.Set<ClassCourse>().Where(c => classCourseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var classIds = classCourses.Values.Select(c => c.ClassId).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var courses = await lookups.CoursesAsync(organizationId, classCourses.Values.Select(c => c.CourseId).Distinct().ToList(), ct);
        var users = await db.Set<User>().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        return rows.Select(r =>
        {
            students.TryGetValue(r.StudentId, out var student);
            classCourses.TryGetValue(r.ClassCourseId, out var cc);
            var cls = cc is null ? null : classes.GetValueOrDefault(cc.ClassId);
            var course = cc is null ? null : courses.GetValueOrDefault(cc.CourseId);
            var requester = users.GetValueOrDefault(r.RequestedBy);
            var decider = r.DecidedBy is { } id ? users.GetValueOrDefault(id) : null;
            return new EnrollmentExceptionDto(
                r.Id, r.ClassCourseId, r.StudentId, student?.StudentCode ?? string.Empty, student?.NameAr ?? string.Empty,
                student?.NameEn ?? string.Empty, course?.NameAr ?? string.Empty, course?.NameEn ?? string.Empty, cls?.NameAr ?? string.Empty,
                cls?.NameEn ?? string.Empty, JsonSerializer.Deserialize<List<string>>(r.FailedTypesJson) ?? [], r.Reason, r.Status,
                requester?.DisplayNameAr, requester?.DisplayNameEn, r.CreatedAt, decider?.DisplayNameAr, decider?.DisplayNameEn,
                r.DecidedAt, r.DecisionComment);
        }).ToList();
    }
}
