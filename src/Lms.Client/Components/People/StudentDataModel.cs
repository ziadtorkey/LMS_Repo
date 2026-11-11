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

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The request data; call only after the form validated (required fields are set).</summary>
    public StudentDataInput ToInput() => new(NameAr.Trim(), NameEn.Trim(), Blank(NicknameAr), Blank(NicknameEn),
        DateOnly.FromDateTime(DateOfBirth!.Value), Gender!, Religion!, Nationality!, Blank(NationalId), Blank(Mobile), Blank(Email), PhotoFileId);
}
