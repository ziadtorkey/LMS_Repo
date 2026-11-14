using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Configuration;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>
/// The one place where a set of field changes becomes new student data: the same validation as an admin edit (field rules, photo
/// visible, national id unique in the client), used for the student's own direct edit, for a request when it is filed, and for a
/// request when it is approved. Nothing is written here; the caller applies the returned data.
/// </summary>
internal sealed class StudentChangeApplier(LmsDbContext db, IPersonDuplicateFinder duplicates, ISettingsResolver settings, IClock clock)
{
    public async Task<IReadOnlyCollection<string>> CriticalAsync(StudentProfile student, CancellationToken ct) =>
        (await settings.ResolveAsync(SettingKeys.StudentsCriticalFields, student.OrgUnitPath, ct)).Value;

    public async Task<(CleanStudentData? Data, IResult? Problem)> PrepareAsync(
        StudentProfile student, IReadOnlyDictionary<string, string?> changes, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var input = StudentFieldValues.ToInput(student);
        foreach (var (field, value) in changes)
        {
            input = StudentFieldValues.Overlay(input, field, value, errors) ?? input;
        }

        if (errors.Count > 0)
        {
            return (null, StudentValidators.Problem(errors));
        }

        if (StudentValidators.Validate(input, DateOnly.FromDateTime(clock.UtcNow), out var clean) is { } invalid)
        {
            return (null, invalid);
        }

        if (changes.ContainsKey(StudentFields.Photo) && clean.PhotoFileId is { } photo && photo != student.PhotoFileId
            && !await db.Set<StoredFile>().AnyAsync(f => f.Id == photo, ct))
        {
            return (null, StudentValidators.Problem(new Dictionary<string, string[]> { [StudentFields.Photo] = ["file_not_found"] }));
        }

        if (changes.ContainsKey(StudentFields.NationalId) && clean.NationalId is { } nationalId && nationalId != student.NationalId
            && await duplicates.StudentNationalIdExistsAsync(student.OrganizationId, nationalId, student.Id, ct))
        {
            return (null, StudentProblems.Duplicate("nationalId"));
        }

        return (clean, null);
    }

    public static void Apply(StudentProfile student, CleanStudentData d) =>
        student.Update(
            d.NameAr, d.NameEn, d.NicknameAr, d.NicknameEn, d.DateOfBirth, d.Gender, d.Religion, d.Nationality, d.NationalId, d.Mobile,
            d.Email, d.PhotoFileId);
}
