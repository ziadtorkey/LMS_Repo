using System.Net.Mail;
using Lms.Domain.People;
using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Api.Features.Students;

/// <summary>The student data after validation, in <see cref="PersonNormalizer"/> form.</summary>
internal sealed record CleanStudentData(
    string NameAr, string NameEn, string? NicknameAr, string? NicknameEn, DateOnly DateOfBirth, string Gender, string Religion,
    string Nationality, string? NationalId, string? Mobile, string? Email, Guid? PhotoFileId);

/// <summary>Field rules of the student bodies (M2 Global Constraints: lengths, catalogs, normalization).</summary>
internal static class StudentValidators
{
    private const int MaxName = 200;

    public static IResult? Validate(StudentDataInput? data, DateOnly today, out CleanStudentData clean)
    {
        clean = null!;
        if (data is null)
        {
            return Problem(new Dictionary<string, string[]> { ["Data"] = ["required"] });
        }

        var errors = new Dictionary<string, string[]>();
        var nameAr = data.NameAr?.Trim() ?? string.Empty;
        var nameEn = data.NameEn?.Trim() ?? string.Empty;
        Name(errors, "NameAr", nameAr, required: true);
        Name(errors, "NameEn", nameEn, required: true);
        Name(errors, "NicknameAr", data.NicknameAr?.Trim(), required: false);
        Name(errors, "NicknameEn", data.NicknameEn?.Trim(), required: false);

        if (data.DateOfBirth > today || data.DateOfBirth.Year < 1990)
        {
            errors["DateOfBirth"] = ["invalid_value"];
        }

        if (!Genders.All.Contains(data.Gender ?? string.Empty))
        {
            errors["Gender"] = ["invalid_value"];
        }

        if (!Religions.All.Contains(data.Religion ?? string.Empty))
        {
            errors["Religion"] = ["invalid_value"];
        }

        if (Nationalities.Find(data.Nationality) is null)
        {
            errors["Nationality"] = ["invalid_value"];
        }

        var nationalId = PersonNormalizer.NationalId(data.NationalId);
        if (!string.IsNullOrWhiteSpace(data.NationalId) && nationalId is null)
        {
            errors["NationalId"] = ["national_id_invalid"];
        }

        var mobile = PersonNormalizer.Mobile(data.Mobile);
        if (!string.IsNullOrWhiteSpace(data.Mobile) && mobile is null)
        {
            errors["Mobile"] = ["mobile_invalid"];
        }

        string? email = null;
        if (!string.IsNullOrWhiteSpace(data.Email))
        {
            email = PersonNormalizer.Email(data.Email);
            if (email is null || email.Length > 256 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            {
                errors["Email"] = ["email_invalid"];
            }
        }

        if (errors.Count > 0)
        {
            return Problem(errors);
        }

        clean = new CleanStudentData(
            nameAr, nameEn, Clean(data.NicknameAr), Clean(data.NicknameEn), data.DateOfBirth, data.Gender!, data.Religion!,
            data.Nationality!, nationalId, mobile, email, data.PhotoFileId);
        return null;
    }

    public static IResult Problem(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.validation_failed });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Name(Dictionary<string, string[]> errors, string field, string? value, bool required)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (required)
            {
                errors[field] = ["required"];
            }
        }
        else if (value.Length > MaxName)
        {
            errors[field] = ["too_long"];
        }
    }
}
