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
             Permissions.IdentityProviders.View, Permissions.IdentityProviders.Manage,
             // M3 (Import only for OrgAdmin, §3.4; Blueprints.* per D46).
             Permissions.Students.View, Permissions.Students.Create, Permissions.Students.Update,
             Permissions.Students.Delete, Permissions.Students.Import, Permissions.Students.ApproveChanges,
             Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Update, Permissions.Courses.Delete,
             Permissions.Courses.Import, Permissions.Courses.Allocate, Permissions.Courses.ManageSupervisors,
             Permissions.Blueprints.View, Permissions.Blueprints.Edit, Permissions.Blueprints.Publish,
             Permissions.Classes.View, Permissions.Classes.Create, Permissions.Classes.Update, Permissions.Classes.Delete,
             Permissions.Classes.Close, Permissions.Classes.ManageCourses,
             Permissions.TeacherAssignments.View, Permissions.TeacherAssignments.Manage,
             Permissions.Enrollment.View, Permissions.Enrollment.Manage, Permissions.Enrollment.Import,
             Permissions.Enrollment.Transfer, Permissions.Enrollment.RequestException,
             Permissions.Enrollment.ApproveException,
             Permissions.Groups.View, Permissions.Groups.Manage, Permissions.LessonPlans.View,
             // M4a: §3.4 says View, but Manage too so the D16 escalation guard lets them assign the Teacher role.
             Permissions.Modules.View, Permissions.Modules.Manage,
             // M4a wave 2 (D52): the staff side of assignments, quizzes, library and forums.
             Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage,
             Permissions.CourseAssignments.Grade,
             Permissions.Quizzes.View, Permissions.Quizzes.Manage, Permissions.Quizzes.Grade,
             Permissions.Library.View, Permissions.Library.Manage,
             Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate]),
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
             Permissions.Staff.View, Permissions.Staff.Create,
             // M3 (no Import, §3.4; Blueprints.* per D46).
             Permissions.Students.View, Permissions.Students.Create, Permissions.Students.Update,
             Permissions.Students.Delete, Permissions.Students.ApproveChanges,
             Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Update, Permissions.Courses.Delete,
             Permissions.Courses.Allocate, Permissions.Courses.ManageSupervisors,
             Permissions.Blueprints.View, Permissions.Blueprints.Edit, Permissions.Blueprints.Publish,
             Permissions.Classes.View, Permissions.Classes.Create, Permissions.Classes.Update, Permissions.Classes.Delete,
             Permissions.Classes.Close, Permissions.Classes.ManageCourses,
             Permissions.TeacherAssignments.View, Permissions.TeacherAssignments.Manage,
             Permissions.Enrollment.View, Permissions.Enrollment.Manage, Permissions.Enrollment.Transfer,
             Permissions.Enrollment.RequestException, Permissions.Enrollment.ApproveException,
             Permissions.Groups.View, Permissions.Groups.Manage, Permissions.LessonPlans.View,
             // M4a: §3.4 says View, but Manage too so the D16 escalation guard lets them assign the Teacher role.
             Permissions.Modules.View, Permissions.Modules.Manage,
             // M4a wave 2 (D52): the staff side of assignments, quizzes, library and forums.
             Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage,
             Permissions.CourseAssignments.Grade,
             Permissions.Quizzes.View, Permissions.Quizzes.Manage, Permissions.Quizzes.Grade,
             Permissions.Library.View, Permissions.Library.Manage,
             Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate]),
        new(Supervisor, RoleBaseKinds.Staff,
            new("المشرف الأكاديمي", "Academic Supervisor"),
            new("رئيس القسم", "Head of Department"),
            [Permissions.OrgUnits.View, Permissions.Settings.View, Permissions.Policies.View,
             Permissions.Files.Upload, Permissions.Files.Download,
             Permissions.LevelTypes.View, Permissions.Templates.View, Permissions.AcademicYears.View,
             Permissions.Staff.View,
             // M3: course rights are limited to supervised courses by RelationshipRules.
             Permissions.Students.View,
             Permissions.Courses.View, Permissions.Courses.Create, Permissions.Courses.Update, Permissions.Courses.Delete,
             Permissions.Courses.Allocate,
             Permissions.Blueprints.View, Permissions.Blueprints.Edit, Permissions.Blueprints.Publish,
             Permissions.Classes.View, Permissions.TeacherAssignments.View, Permissions.TeacherAssignments.Manage,
             Permissions.Enrollment.View, Permissions.Enrollment.ApproveException, Permissions.Groups.View,
             Permissions.LessonPlans.View,
             // M4a (§3.4 "Content & activities": View).
             Permissions.Modules.View,
             // M4a wave 2: read activities; manage the library (FR-LIB-01 "managed by Supervisor/Admin/Teacher").
             Permissions.CourseAssignments.View, Permissions.Quizzes.View, Permissions.Library.View,
             Permissions.Library.Manage, Permissions.Forums.View]),
        new(Counselor, RoleBaseKinds.Staff,
            new("الأخصائي الاجتماعي", "Social Worker"),
            new("شؤون الطلاب", "Student Affairs"),
            [Permissions.Files.Upload, Permissions.Files.Download, Permissions.AcademicYears.View, Permissions.Staff.View,
             Permissions.Students.View, Permissions.Students.Update, Permissions.Students.ApproveChanges,
             Permissions.Classes.View, Permissions.Enrollment.View, Permissions.Enrollment.RequestException,
             Permissions.Groups.View,
             // M4a wave 2: every staff role reads the library.
             Permissions.Library.View]),
        new(Teacher, RoleBaseKinds.Staff,
            new("المعلم", "Teacher"),
            new("عضو هيئة التدريس", "Instructor"),
            // M3: relationship-scoped to own ClassCourses (D32, RelationshipRules).
            [Permissions.Files.Upload, Permissions.Files.Download, Permissions.AcademicYears.View,
             Permissions.Students.View, Permissions.Courses.View, Permissions.Blueprints.View, Permissions.Classes.View,
             Permissions.TeacherAssignments.View, Permissions.Enrollment.View, Permissions.Groups.View,
             Permissions.Groups.Manage, Permissions.LessonPlans.View,
             // M4a: CRUD on modules of own ClassCourses (§3.4 "Content & activities", RelationshipRules).
             Permissions.Modules.View, Permissions.Modules.Manage,
             // M4a wave 2: activities of own ClassCourses (RelationshipRules); the library is unit-wide.
             Permissions.CourseAssignments.View, Permissions.CourseAssignments.Manage,
             Permissions.CourseAssignments.Grade,
             Permissions.Quizzes.View, Permissions.Quizzes.Manage, Permissions.Quizzes.Grade,
             Permissions.Library.View, Permissions.Library.Manage,
             Permissions.Forums.View, Permissions.Forums.Post, Permissions.Forums.Moderate]),
        new(Student, RoleBaseKinds.Student,
            new("الطالب", "Student"),
            new("الطالب", "Student"),
            [Permissions.Files.Upload, Permissions.Files.Download, Permissions.AcademicYears.View,
             Permissions.Students.SelfEdit, Permissions.LessonPlans.View,
             // M4a: own enrollments only (RelationshipRules).
             Permissions.Modules.View,
             // M4a wave 2: own enrollments only (RelationshipRules); Submit and Attempt are SelfService.
             Permissions.CourseAssignments.View, Permissions.CourseAssignments.Submit,
             Permissions.Quizzes.View, Permissions.Quizzes.Attempt,
             Permissions.Library.View, Permissions.Forums.View, Permissions.Forums.Post]),
        new(Guardian, RoleBaseKinds.Guardian,
            new("ولي الأمر", "Parent"),
            new("ولي الأمر", "Parent"),
            [Permissions.Files.Download, Permissions.AcademicYears.View, Permissions.Students.ViewChildren]),
    ];

    public static BuiltInRoleDefinition Get(string key) =>
        All.FirstOrDefault(r => r.Key == key)
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown built-in role.");
}
