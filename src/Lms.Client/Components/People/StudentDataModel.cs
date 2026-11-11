using Lms.Shared.People;

namespace Lms.Client.Components.People;

/// <summary>The editable data of a student while a form is open (create now, edit with the profile task).</summary>
public sealed class StudentDataModel
{
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NicknameAr { get; set; } = "";
    public string NicknameEn { get; set; } = "";
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Religion { get; set; }
    public string? Nationality { get; set; }
    public string NationalId { get; set; } = "";
    public string Mobile { get; set; } = "";
    public string Email { get; set; } = "";
    public Guid? PhotoFileId { get; set; }

    /// <summary>Fills the model from a stored student (the edit dialog).</summary>
    public static StudentDataModel From(StudentDetailDto d) => new()
    {
        NameAr = d.Summary.NameAr, NameEn = d.Summary.NameEn, NicknameAr = d.NicknameAr ?? "", NicknameEn = d.NicknameEn ?? "",
        DateOfBirth = d.Summary.DateOfBirth.ToDateTime(TimeOnly.MinValue), Gender = d.Summary.Gender, Religion = d.Religion,
        Nationality = d.Nationality, NationalId = d.NationalId ?? "", Mobile = d.Mobile ?? "", Email = d.Email ?? "",
        PhotoFileId = d.Summary.PhotoFileId,
    };

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The request data; call only after the form validated (required fields are set).</summary>
    public StudentDataInput ToInput() => new(NameAr.Trim(), NameEn.Trim(), Blank(NicknameAr), Blank(NicknameEn),
        DateOnly.FromDateTime(DateOfBirth!.Value), Gender!, Religion!, Nationality!, Blank(NationalId), Blank(Mobile), Blank(Email), PhotoFileId);
}
