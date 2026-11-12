using Lms.Api.Features.Guardians;
using Lms.Domain.Classes;
using Lms.Domain.Identity;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.People.Guardians;
using Lms.Infrastructure.People.Students;
using Lms.Infrastructure.Persistence;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students;

/// <summary>Builds the student DTOs from profiles. Every lookup goes through the caller's scope.</summary>
public sealed class StudentMapper(LmsDbContext db)
{
    public async Task<IReadOnlyList<StudentSummaryDto>> SummariesAsync(
        IReadOnlyList<StudentProfile> students, Guid? academicYearId, CancellationToken ct)
    {
        var ids = students.Select(s => s.Id).ToList();
        var unitIds = students.Select(s => s.OrgUnitId).Distinct().ToList();
        var units = await db.Set<OrgUnit>().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);

        // primary guardian of each student: only through guardians visible in the caller's scope
        var primaryLinks = await db.Set<StudentGuardian>().Where(l => ids.Contains(l.StudentId) && l.IsPrimary).ToListAsync(ct);
        var guardianIds = primaryLinks.Select(l => l.GuardianId).ToList();
        var guardians = await GuardianVisibility.Visible(db).Where(g => guardianIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        // current class: the active placement (of the queried year when given)
        var placements = await db.Set<ClassStudent>()
            .Where(c => ids.Contains(c.StudentId) && c.Status == ClassStudent.ActiveStatus
                        && (academicYearId == null || c.AcademicYearId == academicYearId))
            .ToListAsync(ct);
        var classIds = placements.Select(p => p.ClassId).Distinct().ToList();
        var classes = await db.Set<Class>().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        return students.Select(s =>
        {
            var home = units.GetValueOrDefault(s.OrgUnitId);
            var link = primaryLinks.FirstOrDefault(l => l.StudentId == s.Id);
            var guardian = link is null ? null : guardians.GetValueOrDefault(link.GuardianId);
            var placement = placements.Where(p => p.StudentId == s.Id).OrderByDescending(p => p.From).FirstOrDefault();
            var cls = placement is null ? null : classes.GetValueOrDefault(placement.ClassId);
            return new StudentSummaryDto(
                s.Id, s.StudentCode, s.NameAr, s.NameEn, s.DateOfBirth, s.Gender, s.OrgUnitId, home?.NameAr ?? string.Empty,
                home?.NameEn ?? string.Empty, s.Status.ToString(), s.PhotoFileId, guardian?.NameAr, guardian?.NameEn, guardian?.Mobile,
                cls?.Id, cls?.NameAr, cls?.NameEn);
        }).ToList();
    }

    public async Task<StudentDetailDto> DetailAsync(StudentProfile student, CancellationToken ct)
    {
        var summary = (await SummariesAsync([student], null, ct))[0];
        User? user = student.UserId is { } userId ? await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) : null;
        var history = await db.Set<PersonStatusChange>().Where(c => c.Kind == PersonKind.Student && c.ProfileId == student.Id)
            .OrderByDescending(c => c.ChangedAt).ToListAsync(ct);
        var changedBy = history.Where(c => c.ChangedBy != null).Select(c => c.ChangedBy!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => changedBy.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return new StudentDetailDto(
            summary, student.NicknameAr, student.NicknameEn, student.Religion, student.Nationality, student.NationalId, student.Mobile,
            student.Email, user is not null, user is { PasswordHash: not null, MustChangePassword: false }, user?.MustChangePassword ?? false,
            StudentFeatureResolver.Parse(student.FeatureFlagOverridesJson),
            await GuardianMapper.LinksOfAsync(db, student, ct),
            history.Select(c =>
            {
                var by = c.ChangedBy is { } id ? names.GetValueOrDefault(id) : null;
                return new PersonStatusChangeDto(c.From.ToString(), c.To.ToString(), c.Reason, c.ChangedAt, by?.DisplayNameAr, by?.DisplayNameEn);
            }).ToList(),
            0);
    }
}
