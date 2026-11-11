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

    // Explicit lists (bug #21, M1 Global Constraints): every permission is added by hand to each role it
    // belongs to (PRD §3.4). Never "all permissions"; never a Permissions.PlatformOnly entry.
    public static IReadOnlyList<BuiltInRoleDefinition> All { get; } =
    [
        new(OrgAdmin, RoleBaseKinds.Staff,
            new("مسؤول النظام", "Admin"),
            new("مسؤول النظام", "Admin"),
            [Permissions.OrgUnits.View, Permissions.OrgUnits.Create, Permissions.OrgUnits.Update, Permissions.OrgUnits.Delete,
             Permissions.OrgUnits.Move,
             Permissions.Roles.View, Permissions.Roles.Create, Permissions.Roles.Update, Permissions.Roles.Delete,
             Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Update,
             Permissions.Assignments.Delete,
             Permissions.Settings.View, Permissions.Settings.Update, Permissions.Policies.View, Permissions.Policies.Update,
             Permissions.Audit.View, Permissions.Audit.Export, Permissions.Files.Upload, Permissions.Files.Download,
             Permissions.LevelTypes.View, Permissions.LevelTypes.Create, Permissions.LevelTypes.Update,
             Permissions.LevelTypes.Delete,
             Permissions.Templates.View, Permissions.Templates.Create, Permissions.Templates.Delete,
             Permissions.Templates.Apply,
             Permissions.AcademicYears.View, Permissions.AcademicYears.Create, Permissions.AcademicYears.Update,
             Permissions.AcademicYears.Delete,
             Permissions.Staff.View, Permissions.Staff.Create, Permissions.Staff.Update, Permissions.Staff.Delete,
             Permissions.Staff.Import,
             Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage]),
        new(Manager, RoleBaseKinds.Staff,
            new("مدير المدرسة", "School Manager"),
            new("العميد", "Dean"),
            // D16: a Manager may assign roles; which roles is limited by the escalation guard (M1 Task 21).
            [Permissions.OrgUnits.View, Permissions.Roles.View,
             Permissions.Assignments.View, Permissions.Assignments.Create, Permissions.Assignments.Delete,
             Permissions.Settings.View, Permissions.Policies.View, Permissions.Audit.View,
             Permissions.Files.Upload, Permissions.Files.Download,
             Permissions.LevelTypes.View, Permissions.Templates.View, Permissions.AcademicYears.View,
             // D32: a Manager keeps Staff View + Create only.
             Permissions.Staff.View, Permissions.Staff.Create]),
        new(Supervisor, RoleBaseKinds.Staff,
            new("المشرف الأكاديمي", "Academic Supervisor"),
            new("رئيس القسم", "Head of Department"),
            [Permissions.OrgUnits.View, Permissions.Settings.View, Permissions.Policies.View,
             Permissions.Files.Upload, Permissions.Files.Download,
             Permissions.LevelTypes.View, Permissions.Templates.View, Permissions.AcademicYears.View,
             Permissions.Staff.View]),
        new(Counselor, RoleBaseKinds.Staff,
            new("الأخصائي الاجتماعي", "Social Worker"),
            new("شؤون الطلاب", "Student Affairs"),
            [Permissions.Files.Upload, Permissions.Files.Download, Permissions.AcademicYears.View, Permissions.Staff.View]),
        new(Teacher, RoleBaseKinds.Staff,
            new("المعلم", "Teacher"),
            new("عضو هيئة التدريس", "Instructor"),
            [Permissions.Files.Upload, Permissions.Files.Download, Permissions.AcademicYears.View]),
        new(Student, RoleBaseKinds.Student,
            new("الطالب", "Student"),
            new("الطالب", "Student"),
            [Permissions.Files.Upload, Permissions.Files.Download, Permissions.AcademicYears.View]),
        new(Guardian, RoleBaseKinds.Guardian,
            new("ولي الأمر", "Parent"),
            new("ولي الأمر", "Parent"),
            [Permissions.Files.Download, Permissions.AcademicYears.View]),
    ];

    public static BuiltInRoleDefinition Get(string key) =>
        All.FirstOrDefault(r => r.Key == key)
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown built-in role.");
}
