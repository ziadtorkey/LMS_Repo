namespace Lms.Shared.People;

/// <summary>
/// Status of a per-client person profile (staff in M2; student and guardian in M3). Only <c>Active</c> profiles keep
/// the role assignments of their base kind valid (M2 plan, person status gate).
/// </summary>
public static class PersonStatuses
{
    public const string Active = "Active";
    public const string Deactivated = "Deactivated";
    public const string Expired = "Expired";

    public static IReadOnlyList<string> All { get; } = [Active, Deactivated, Expired];
}

public static class Genders
{
    public const string Male = "Male";
    public const string Female = "Female";

    public static IReadOnlyList<string> All { get; } = [Male, Female];
}

public static class Religions
{
    public const string Muslim = "Muslim";
    public const string Christian = "Christian";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } = [Muslim, Christian, Other];
}

public static class GuardianRelations
{
    public const string Father = "Father";
    public const string Mother = "Mother";
    public const string Brother = "Brother";
    public const string Sister = "Sister";
    public const string Grandfather = "Grandfather";
    public const string Grandmother = "Grandmother";
    public const string Uncle = "Uncle";
    public const string Aunt = "Aunt";
    public const string Other = "Other";

    public static IReadOnlyList<string> All { get; } =
        [Father, Mother, Brother, Sister, Grandfather, Grandmother, Uncle, Aunt, Other];
}

/// <summary>Values of the setting <c>Students.CodeMode</c> (D25).</summary>
public static class StudentCodeModes
{
    public const string Auto = "Auto";
    public const string Manual = "Manual";

    public static IReadOnlyList<string> All { get; } = [Auto, Manual];
}

/// <summary>Student data fields (FR-STU-04): which a student may request to change, and which are critical by default.</summary>
public static class StudentFields
{
    public const string NameAr = "NameAr";
    public const string NameEn = "NameEn";
    public const string NicknameAr = "NicknameAr";
    public const string NicknameEn = "NicknameEn";
    public const string DateOfBirth = "DateOfBirth";
    public const string Gender = "Gender";
    public const string Religion = "Religion";
    public const string Nationality = "Nationality";
    public const string NationalId = "NationalId";
    public const string StudentCode = "StudentCode";
    public const string Mobile = "Mobile";
    public const string Email = "Email";
    public const string Photo = "Photo";

    public static IReadOnlyList<string> All { get; } =
    [
        NameAr, NameEn, NicknameAr, NicknameEn, DateOfBirth, Gender, Religion, Nationality, NationalId, StudentCode,
        Mobile, Email, Photo,
    ];

    /// <summary>Fields a student can edit or request to change (every field except the code and the account email).</summary>
    public static IReadOnlyList<string> Requestable { get; } =
        [NameAr, NameEn, NicknameAr, NicknameEn, DateOfBirth, Gender, Religion, Nationality, NationalId, Mobile, Photo];

    /// <summary>Default of <c>Students.CriticalFields</c>: changes to these need approval.</summary>
    public static IReadOnlyList<string> DefaultCritical { get; } =
        [NameAr, NameEn, DateOfBirth, Gender, Religion, Nationality, NationalId];
}

public static class ChangeRequestStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";

    public static IReadOnlyList<string> All { get; } = [Pending, Approved, Rejected, Cancelled];
}

/// <summary>Accepted values of <c>StudentQuery.Sort</c> (a leading <c>-</c> sorts descending).</summary>
public static class StudentSorts
{
    public const string Name = "name";
    public const string NameDesc = "-name";
    public const string Code = "code";
    public const string CodeDesc = "-code";

    public static IReadOnlyList<string> All { get; } = [Name, NameDesc, Code, CodeDesc];
}

/// <summary>Accepted values of <c>StaffQuery.Sort</c> (a leading <c>-</c> sorts descending).</summary>
public static class StaffSorts
{
    public const string Name = "name";
    public const string NameDesc = "-name";
    public const string ExpiresOn = "expiresOn";
    public const string ExpiresOnDesc = "-expiresOn";

    public static IReadOnlyList<string> All { get; } = [Name, NameDesc, ExpiresOn, ExpiresOnDesc];
}
