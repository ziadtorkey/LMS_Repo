using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Navigation;

/// <summary>All navigation entries; later milestones append items here.</summary>
public static class NavCatalog
{
    public static readonly IReadOnlyList<NavItem> Items =
    [
        new("Nav_Home", "", Icons.Material.Filled.Home, null),
        new("Nav_OrgUnits", "org-units", Icons.Material.Filled.AccountTree, Permissions.OrgUnits.View),
        new("Nav_LevelTypes", "level-types", Icons.Material.Filled.Layers, Permissions.LevelTypes.View),
        new("Nav_Exceptions", "enrollment-exceptions", Icons.Material.Filled.Gavel, Permissions.Enrollment.ApproveException),
        new("Nav_Guardians", "guardians", Icons.Material.Filled.FamilyRestroom, Permissions.Students.View),
        new("Nav_Staff", "staff", Icons.Material.Filled.Badge, Permissions.Staff.View),
        new("Nav_MyChildren", "me/children", Icons.Material.Filled.FamilyRestroom, Permissions.Students.ViewChildren),
        new("Nav_MyProfile", "me/profile", Icons.Material.Filled.Person, Permissions.Students.SelfEdit),
        new("Nav_ChangeRequests", "student-change-requests", Icons.Material.Filled.RuleFolder, Permissions.Students.ApproveChanges),
        new("Nav_Students", "students", Icons.Material.Filled.School, Permissions.Students.View),
        new("Nav_Courses", "courses", Icons.Material.Filled.MenuBook, Permissions.Courses.View),
        new("Nav_MyCourses", "me/courses", Icons.Material.Filled.FactCheck, Permissions.Courses.View),
        new("Nav_Classes", "classes", Icons.Material.Filled.Groups, Permissions.Classes.View),
        new("Nav_MyClassCourses", "me/class-courses", Icons.Material.Filled.AutoStories, Permissions.LessonPlans.View),
        new("Nav_Library", "library", Icons.Material.Filled.LocalLibrary, Permissions.Library.View),
        new("Nav_Roles", "roles", Icons.Material.Filled.AdminPanelSettings, Permissions.Roles.View),
        new("Nav_Assignments", "assignments", Icons.Material.Filled.GroupAdd, Permissions.Assignments.View),
        new("Nav_AcademicYears", "academic-years", Icons.Material.Filled.CalendarMonth, Permissions.AcademicYears.View),
        new("Nav_Templates", "templates", Icons.Material.Filled.ContentCopy, Permissions.Templates.View),
        new("Nav_IdentityProviders", "identity-providers", Icons.Material.Filled.Login, Permissions.IdentityProviders.View),
        new("Nav_Settings", "settings", Icons.Material.Filled.Settings, Permissions.Settings.View),
        new("Nav_Audit", "audit", Icons.Material.Filled.History, Permissions.Audit.View),
        new("Nav_Clients", "clients", Icons.Material.Filled.Business, null, PlatformAdminOnly: true),
    ];
}
