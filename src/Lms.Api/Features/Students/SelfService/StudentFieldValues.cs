using System.Globalization;
using Lms.Domain.People;
using Lms.Shared.People;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>Reads and overlays the requestable student fields as strings (<c>yyyy-MM-dd</c> dates, file ids as GUIDs).</summary>
internal static class StudentFieldValues
{
    public static string? Read(StudentProfile s, string field) => field switch
    {
        StudentFields.NameAr => s.NameAr,
        StudentFields.NameEn => s.NameEn,
        StudentFields.NicknameAr => s.NicknameAr,
        StudentFields.NicknameEn => s.NicknameEn,
        StudentFields.DateOfBirth => s.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        StudentFields.Gender => s.Gender,
        StudentFields.Religion => s.Religion,
        StudentFields.Nationality => s.Nationality,
        StudentFields.NationalId => s.NationalId,
        StudentFields.Mobile => s.Mobile,
        StudentFields.Photo => s.PhotoFileId?.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    public static string? ReadClean(CleanStudentData d, string field) => field switch
    {
        StudentFields.NameAr => d.NameAr,
        StudentFields.NameEn => d.NameEn,
        StudentFields.NicknameAr => d.NicknameAr,
        StudentFields.NicknameEn => d.NicknameEn,
        StudentFields.DateOfBirth => d.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        StudentFields.Gender => d.Gender,
        StudentFields.Religion => d.Religion,
        StudentFields.Nationality => d.Nationality,
        StudentFields.NationalId => d.NationalId,
        StudentFields.Mobile => d.Mobile,
        StudentFields.Photo => d.PhotoFileId?.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    /// <summary>Null and blank are the same "empty" when comparing stored and requested values.</summary>
    public static bool Same(string? a, string? b) =>
        string.Equals(string.IsNullOrWhiteSpace(a) ? null : a, string.IsNullOrWhiteSpace(b) ? null : b, StringComparison.Ordinal);

    public static StudentDataInput ToInput(StudentProfile s) => new(
        s.NameAr, s.NameEn, s.NicknameAr, s.NicknameEn, s.DateOfBirth, s.Gender, s.Religion, s.Nationality, s.NationalId, s.Mobile,
        s.Email, s.PhotoFileId);

    /// <summary>The data with one field replaced; a value that cannot be parsed (a date, a file id) is reported by field name.</summary>
    public static StudentDataInput? Overlay(StudentDataInput d, string field, string? value, Dictionary<string, string[]> errors)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        switch (field)
        {
            case StudentFields.NameAr: return d with { NameAr = text ?? string.Empty };
            case StudentFields.NameEn: return d with { NameEn = text ?? string.Empty };
            case StudentFields.NicknameAr: return d with { NicknameAr = text };
            case StudentFields.NicknameEn: return d with { NicknameEn = text };
            case StudentFields.Gender: return d with { Gender = text ?? string.Empty };
            case StudentFields.Religion: return d with { Religion = text ?? string.Empty };
            case StudentFields.Nationality: return d with { Nationality = text ?? string.Empty };
            case StudentFields.NationalId: return d with { NationalId = text };
            case StudentFields.Mobile: return d with { Mobile = text };
            case StudentFields.DateOfBirth:
                if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                {
                    return d with { DateOfBirth = dob };
                }

                break;
            case StudentFields.Photo:
                if (text is null)
                {
                    return d with { PhotoFileId = null };
                }

                if (Guid.TryParse(text, out var photo))
                {
                    return d with { PhotoFileId = photo };
                }

                break;
        }

        errors[field] = ["invalid_value"];
        return null;
    }
}
