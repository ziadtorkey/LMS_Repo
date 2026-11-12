using Lms.Client.Services;
using Lms.Shared.People;

namespace Lms.Client.Components.People;

/// <summary>Display text of a student field value (change request before/after, read-only fields).</summary>
public static class StudentFieldText
{
    /// <summary>Catalog keys and country codes are shown by their localized names; empty is a dash; a photo is shown as a marker.</summary>
    public static string Display(string field, string? value, Func<string, string> text)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        return field switch
        {
            StudentFields.Gender => text($"Gender_{value}"),
            StudentFields.Religion => text($"Religion_{value}"),
            StudentFields.Nationality => Nationalities.Find(value) is { } n ? CultureText.Pick(n.NameAr, n.NameEn) : value,
            StudentFields.Photo => text("Person_Photo"),
            _ => value,
        };
    }

    /// <summary>The stored value of a field in the string form used by change requests (yyyy-MM-dd dates, catalog keys, file ids).</summary>
    public static string? Current(StudentDetailDto d, string field) => field switch
    {
        StudentFields.NameAr => d.Summary.NameAr,
        StudentFields.NameEn => d.Summary.NameEn,
        StudentFields.NicknameAr => d.NicknameAr,
        StudentFields.NicknameEn => d.NicknameEn,
        StudentFields.DateOfBirth => d.Summary.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        StudentFields.Gender => d.Summary.Gender,
        StudentFields.Religion => d.Religion,
        StudentFields.Nationality => d.Nationality,
        StudentFields.NationalId => d.NationalId,
        StudentFields.Mobile => d.Mobile,
        StudentFields.Photo => d.Summary.PhotoFileId?.ToString(),
        _ => null,
    };
}
