using Lms.Shared.Authorization;
using MudBlazor;

namespace Lms.Client.Navigation;

/// <summary>All navigation entries, grouped into sidebar sections (Stitch, #561); later milestones append items here.</summary>
public static class NavCatalog
{
    public const string Structure = "NavSection_Structure";
    public const string People = "NavSection_People";
    public const string Academic = "NavSection_Academic";
    public const string Security = "NavSection_Security";
    public const string Communication = "NavSection_Communication";

    /// <summary>Section order in the sidebar.</summary>
    public static readonly IReadOnlyList<string> Sections = [Structure, People, Academic, Communication, Security];

    public static readonly IReadOnlyList<NavItem> Items =
    [
        new("Nav_Home", "", Icons.Material.Rounded.Home, null),
        new("Nav_OrgUnits", "org-units", Icons.Material.Rounded.AccountTree, Permissions.OrgUnits.View, Section: Structure),
        new("Nav_LevelTypes", "level-types", Icons.Material.Rounded.Layers, Permissions.LevelTypes.View, Section: Structure),
        new("Nav_AcademicYears", "academic-years", Icons.Material.Rounded.CalendarMonth, Permissions.AcademicYears.View, Section: Structure),
        new("Nav_Templates", "templates", Icons.Material.Rounded.ContentCopy, Permissions.Templates.View, Section: Structure),
        new("Nav_Staff", "staff", Icons.Material.Rounded.Badge, Permissions.Staff.View, Section: People),
        new("Nav_Students", "students", Icons.Material.Rounded.School, Permissions.Students.View, Section: People),
        new("Nav_Guardians", "guardians", Icons.Material.Rounded.FamilyRestroom, Permissions.Students.View, Section: People),
        new("Nav_MyChildren", "me/children", Icons.Material.Rounded.FamilyRestroom, Permissions.Students.ViewChildren, Section: People),
        new("Nav_ChangeRequests", "student-change-requests", Icons.Material.Rounded.RuleFolder, Permissions.Students.ApproveChanges, Section: People),
        new("Nav_Exceptions", "enrollment-exceptions", Icons.Material.Rounded.Gavel, Permissions.Enrollment.ApproveException, Section: People),
        new("Nav_MyProfile", "me/profile", Icons.Material.Rounded.Person, Permissions.Students.SelfEdit, Section: People),
        new("Nav_Courses", "courses", Icons.Material.Rounded.MenuBook, Permissions.Courses.View, Section: Academic),
        new("Nav_MyCourses", "me/courses", Icons.Material.Rounded.FactCheck, Permissions.Courses.View, Section: Academic),
        new("Nav_Classes", "classes", Icons.Material.Rounded.Groups, Permissions.Classes.View, Section: Academic),
        new("Nav_Eligibility", "teaching/eligibility", Icons.Material.Rounded.HowToReg, Permissions.TeacherAssignments.View, Section: Academic),
        new("Nav_MyClassCourses", "me/class-courses", Icons.Material.Rounded.AutoStories, Permissions.LessonPlans.View, Section: Academic),
        new("Nav_Library", "library", Icons.Material.Rounded.LocalLibrary, Permissions.Library.View, Section: Academic),
        new("Nav_Messages", "messages", Icons.Material.Rounded.Forum, Permissions.Messaging.View, Section: Communication),
        new("Nav_Announcements", "announcements", Icons.Material.Rounded.Campaign, Permissions.Announcements.View, Section: Communication),
        new("Nav_MessagingRules", "settings/messaging-rules", Icons.Material.Rounded.Rule, Permissions.Comms.Manage, Section: Security),
        new("Nav_CommunicationSettings", "settings/communication", Icons.Material.Rounded.Tune, Permissions.Comms.Manage, Section: Security),
        new("Nav_Roles", "roles", Icons.Material.Rounded.AdminPanelSettings, Permissions.Roles.View, Section: Security),
        new("Nav_Assignments", "assignments", Icons.Material.Rounded.GroupAdd, Permissions.Assignments.View, Section: Security),
        new("Nav_IdentityProviders", "identity-providers", Icons.Material.Rounded.Login, Permissions.IdentityProviders.View, Section: Security),
        new("Nav_Settings", "settings", Icons.Material.Rounded.Settings, Permissions.Settings.View, Section: Security),
        new("Nav_Audit", "audit", Icons.Material.Rounded.History, Permissions.Audit.View, Section: Security),
        new("Nav_Clients", "clients", Icons.Material.Rounded.Business, null, PlatformAdminOnly: true, Section: Security),
    ];
}
