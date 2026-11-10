namespace Lms.Shared.Authorization;

/// <summary>Default label of a built-in role for one organization type.</summary>
public sealed record RoleLabel(string NameAr, string NameEn);

/// <summary>Definition of a built-in role (PRD §3.1, §3.4). Permission sets become editable per client in M1.</summary>
public sealed record BuiltInRoleDefinition(
    string Key,
    string BaseKind,
    RoleLabel SchoolGroupLabel,
    RoleLabel UniversityLabel,
    IReadOnlyList<string> DefaultPermissions);

public static class BuiltInRoles
{
    public const string OrgAdmin = "OrgAdmin";
    public const string Manager = "Manager";
    public const string Supervisor = "Supervisor";
    public const string Counselor = "Counselor";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public const string Guardian = "Guardian";

    private static readonly string[] FilesBoth = [Permissions.Files.Upload, Permissions.Files.Download];

    public static IReadOnlyList<BuiltInRoleDefinition> All { get; } =
    [
        new(OrgAdmin, RoleBaseKinds.Staff,
            new("مدير النظام", "Admin"),
            new("مدير النظام", "Admin"),
            Permissions.All),
        new(Manager, RoleBaseKinds.Staff,
            new("مدير المدرسة", "School Manager"),
            new("العميد", "Dean"),
            [Permissions.OrgUnits.View, Permissions.Roles.View, Permissions.Assignments.View,
             Permissions.Settings.View, Permissions.Policies.View, Permissions.Audit.View, .. FilesBoth]),
        new(Supervisor, RoleBaseKinds.Staff,
            new("المشرف الأكاديمي", "Academic Supervisor"),
            new("رئيس القسم", "Head of Department"),
            [Permissions.OrgUnits.View, Permissions.Settings.View, Permissions.Policies.View, .. FilesBoth]),
        new(Counselor, RoleBaseKinds.Staff,
            new("الأخصائي الاجتماعي", "Social Worker"),
            new("شؤون الطلاب", "Student Affairs"),
            FilesBoth),
        new(Teacher, RoleBaseKinds.Staff,
            new("المعلم", "Teacher"),
            new("عضو هيئة التدريس", "Instructor"),
            FilesBoth),
        new(Student, RoleBaseKinds.Student,
            new("الطالب", "Student"),
            new("الطالب", "Student"),
            FilesBoth),
        new(Guardian, RoleBaseKinds.Guardian,
            new("ولي الأمر", "Parent"),
            new("ولي الأمر", "Parent"),
            [Permissions.Files.Download]),
    ];

    public static BuiltInRoleDefinition Get(string key) =>
        All.FirstOrDefault(r => r.Key == key)
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown built-in role.");
}
