using System.Globalization;
using Lms.Domain.People;
using Lms.Infrastructure.Imports;
using Lms.Shared.People;

namespace Lms.Api.Features.Students.Import;

/// <summary>The columns of the student template (FR-STU-05) and the readers of their cells (catalog keys or localized labels).</summary>
internal static class StudentImportColumns
{
    public const string StudentCode = nameof(StudentCode), NameAr = nameof(NameAr), NameEn = nameof(NameEn),
        DateOfBirth = nameof(DateOfBirth), Gender = nameof(Gender), Religion = nameof(Religion), Nationality = nameof(Nationality),
        NationalId = nameof(NationalId), Mobile = nameof(Mobile), Email = nameof(Email), UnitCode = nameof(UnitCode),
        ClassCode = nameof(ClassCode), DefaultPassword = nameof(DefaultPassword);

    public static string G(int n, string part) => $"Guardian{n}{part}";

    public const string GName = "NameAr", GNameEn = "NameEn", GRelation = "Relation", GMobile = "Mobile", GEmail = "Email", GNationalId = "NationalId";

    public static readonly string[] GuardianParts = [GName, GNameEn, GRelation, GMobile, GEmail, GNationalId];

    public static IReadOnlyList<ImportColumn> All { get; } = Build();

    private static List<ImportColumn> Build()
    {
        var columns = new List<ImportColumn>
        {
            new(StudentCode, "كود الطالب", "Student code", false, AsText: true, ExampleValue: "S2026001"),
            new(NameAr, "الاسم بالعربية", "Name (Arabic)", true),
            new(NameEn, "الاسم بالإنجليزية", "Name (English)", true),
            new(DateOfBirth, "تاريخ الميلاد", "Date of birth", true, ExampleValue: "2014-03-04"),
            new(Gender, "النوع", "Gender", true, ExampleValue: "Male"),
            new(Religion, "الديانة", "Religion", true, ExampleValue: "Muslim"),
            new(Nationality, "الجنسية", "Nationality", true, ExampleValue: "EG"),
            new(NationalId, "رقم الهوية الوطنية", "National ID", false, AsText: true),
            new(Mobile, "رقم الجوال", "Mobile", false, AsText: true),
            new(Email, "البريد الإلكتروني", "Email", false),
            new(UnitCode, "رمز الوحدة", "Unit code", true, AsText: true),
            new(ClassCode, "رمز الفصل", "Class code", false, AsText: true),
            new(DefaultPassword, "كلمة المرور الافتراضية", "Default password", false, AsText: true, Sensitive: true),
        };
        foreach (var n in new[] { 1, 2 })
        {
            columns.Add(new(G(n, GName), $"اسم ولي الأمر {n} بالعربية", $"Guardian {n} name (Arabic)", false));
            columns.Add(new(G(n, GNameEn), $"اسم ولي الأمر {n} بالإنجليزية", $"Guardian {n} name (English)", false));
            columns.Add(new(G(n, GRelation), $"صلة القرابة {n}", $"Guardian {n} relation", false, GuardianRelations.All, ExampleValue: n == 1 ? "Father" : null));
            columns.Add(new(G(n, GMobile), $"جوال ولي الأمر {n}", $"Guardian {n} mobile", false, AsText: true));
            columns.Add(new(G(n, GEmail), $"بريد ولي الأمر {n}", $"Guardian {n} email", false));
            columns.Add(new(G(n, GNationalId), $"هوية ولي الأمر {n}", $"Guardian {n} national ID", false, AsText: true));
        }

        return columns;
    }

    public static string? Gen(string? value) => Match(value, Genders.All, ("ذكر", Genders.Male), ("أنثى", Genders.Female));

    public static string? Rel(string? value) =>
        Match(value, Religions.All, ("مسلم", Religions.Muslim), ("مسيحي", Religions.Christian), ("أخرى", Religions.Other));

    /// <summary>An ISO code, or the Arabic or English name.</summary>
    public static string? Nat(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (Nationalities.Find(text.ToUpperInvariant()) is { } byCode)
        {
            return byCode.Code;
        }

        return Nationalities.All.FirstOrDefault(n =>
            string.Equals(n.NameAr, text, StringComparison.OrdinalIgnoreCase) || string.Equals(n.NameEn, text, StringComparison.OrdinalIgnoreCase))?.Code;
    }

    public static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    public static bool GroupFilled(ImportRowValues row, int n) => GuardianParts.Any(p => !string.IsNullOrWhiteSpace(row.Get(G(n, p))));

    private static string? Match(string? value, IReadOnlyList<string> keys, params (string Label, string Key)[] labels)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return keys.FirstOrDefault(k => string.Equals(k, text, StringComparison.OrdinalIgnoreCase))
               ?? labels.FirstOrDefault(l => string.Equals(l.Label, text, StringComparison.Ordinal)).Key;
    }
}
