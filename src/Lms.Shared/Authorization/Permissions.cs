using System.Collections.Immutable;
using System.Reflection;

namespace Lms.Shared.Authorization;

/// <summary>
/// Fixed permission catalog. Values follow "Module.Action" (PRD §3.3).
/// Later milestones append nested classes; never rename an existing value.
/// </summary>
public static class Permissions
{
    public static class OrgUnits
    {
        public const string View = "OrgUnits.View";
        public const string Create = "OrgUnits.Create";
        public const string Update = "OrgUnits.Update";
        public const string Delete = "OrgUnits.Delete";
        public const string Move = "OrgUnits.Move";
    }

    public static class Roles
    {
        public const string View = "Roles.View";
        public const string Create = "Roles.Create";
        public const string Update = "Roles.Update";
        public const string Delete = "Roles.Delete";
    }

    public static class Assignments
    {
        public const string View = "Assignments.View";
        public const string Create = "Assignments.Create";
        public const string Update = "Assignments.Update";
        public const string Delete = "Assignments.Delete";
    }

    public static class Settings
    {
        public const string View = "Settings.View";
        public const string Update = "Settings.Update";
    }

    public static class Policies
    {
        public const string View = "Policies.View";
        public const string Update = "Policies.Update";
    }

    public static class Audit
    {
        public const string View = "Audit.View";
        public const string Export = "Audit.Export";
    }

    public static class Files
    {
        public const string Upload = "Files.Upload";
        public const string Download = "Files.Download";
    }

    /// <summary>Platform Admin only (see <see cref="PlatformOnly"/>).</summary>
    public static class Clients
    {
        public const string View = "Clients.View";
        public const string Create = "Clients.Create";
        public const string Update = "Clients.Update";
    }

    public static class LevelTypes
    {
        public const string View = "LevelTypes.View";
        public const string Create = "LevelTypes.Create";
        public const string Update = "LevelTypes.Update";
        public const string Delete = "LevelTypes.Delete";
    }

    public static class Templates
    {
        public const string View = "Templates.View";
        public const string Create = "Templates.Create";
        public const string Delete = "Templates.Delete";
        public const string Apply = "Templates.Apply";
    }

    public static class AcademicYears
    {
        public const string View = "AcademicYears.View";
        public const string Create = "AcademicYears.Create";
        public const string Update = "AcademicYears.Update";
        public const string Delete = "AcademicYears.Delete";

        /// <summary>M7 wave 1 (FR-YEAR-04, D79): close a year (locks grades and attendance, opens the promotion). Org Admin only (D17); there is no reopen in v1.</summary>
        public const string Close = "AcademicYears.Close";
    }

    // M2 People (D23: the Students.* permissions arrive with the M3 contract).
    public static class Staff
    {
        public const string View = "Staff.View";
        public const string Create = "Staff.Create";
        public const string Update = "Staff.Update";
        public const string Delete = "Staff.Delete";
        public const string Import = "Staff.Import";
    }

    /// <summary>A client's own OpenID Connect / Azure AD sign-in providers (FR-SEC-08, D33).</summary>
    public static class IdentityProviders
    {
        public const string View = "IdentityProviders.View";
        public const string Manage = "IdentityProviders.Manage";
    }

    // M3 Academic Structure (M3 plan Task 1; Students.* deferred from M2 by D23).
    public static class Students
    {
        public const string View = "Students.View";
        public const string Create = "Students.Create";
        public const string Update = "Students.Update";
        public const string Delete = "Students.Delete";
        public const string Import = "Students.Import";
        public const string ApproveChanges = "Students.ApproveChanges";

        /// <summary>A student edits their own non-critical data and requests changes to critical data (FR-STU-04).</summary>
        public const string SelfEdit = "Students.SelfEdit";

        /// <summary>A guardian reads their own children (FR-STU-06).</summary>
        public const string ViewChildren = "Students.ViewChildren";

        /// <summary>M7 wave 1 (FR-STU-08, D82): withdraw or graduate a student (Org Admin, Manager, Counselor).</summary>
        public const string Withdraw = "Students.Withdraw";

        /// <summary>M7 wave 1 (FR-STU-08): re-admit a withdrawn or graduated student into a class (Org Admin, Manager).</summary>
        public const string Readmit = "Students.Readmit";

        /// <summary>M7 wave 1 (FR-STU-08): search the read-only archive of students who left.</summary>
        public const string ViewArchive = "Students.ViewArchive";

        /// <summary>M7 wave 2 (D82, D18): edit the client's list of withdrawal reasons (PUT <c>/api/withdrawal-reasons</c>); the client-root Org Admin only. Reading the list needs <see cref="Withdraw"/>.</summary>
        public const string ManageWithdrawalReasons = "Students.ManageWithdrawalReasons";
    }

    public static class Courses
    {
        public const string View = "Courses.View";
        public const string Create = "Courses.Create";
        public const string Update = "Courses.Update";
        public const string Delete = "Courses.Delete";
        public const string Import = "Courses.Import";
        public const string Allocate = "Courses.Allocate";
        public const string ManageSupervisors = "Courses.ManageSupervisors";
    }

    public static class Blueprints
    {
        public const string View = "Blueprints.View";
        public const string Edit = "Blueprints.Edit";
        public const string Publish = "Blueprints.Publish";
    }

    public static class Classes
    {
        public const string View = "Classes.View";
        public const string Create = "Classes.Create";
        public const string Update = "Classes.Update";
        public const string Delete = "Classes.Delete";
        public const string Close = "Classes.Close";
        public const string ManageCourses = "Classes.ManageCourses";
    }

    public static class TeacherAssignments
    {
        public const string View = "TeacherAssignments.View";
        public const string Manage = "TeacherAssignments.Manage";
    }

    public static class Enrollment
    {
        public const string View = "Enrollment.View";
        public const string Manage = "Enrollment.Manage";
        public const string Import = "Enrollment.Import";
        public const string Transfer = "Enrollment.Transfer";
        public const string RequestException = "Enrollment.RequestException";
        public const string ApproveException = "Enrollment.ApproveException";
    }

    public static class Groups
    {
        public const string View = "Groups.View";
        public const string Manage = "Groups.Manage";
    }

    public static class LessonPlans
    {
        public const string View = "LessonPlans.View";
    }

    // M4a Teaching & Learning, wave 1 (M4a plan Task 1). Assets reuse Files.Upload / Files.Download; the asset URL is
    // additionally checked against the owning module (Modules.View) or class course.
    /// <summary>Lesson modules under the lessons of a class lesson plan (FR-LP-05, FR-CNT-01..04).</summary>
    public static class Modules
    {
        /// <summary>Open modules and their content; students also mark completion and read their own progress.</summary>
        public const string View = "Modules.View";

        /// <summary>Add, edit, order, hide, schedule, target and delete modules.</summary>
        public const string Manage = "Modules.Manage";
    }

    // M4a Teaching & Learning, wave 2 (M4a plan Task 2). "CourseAssignments", not "Assignments": Assignments.* are the
    // M1 role assignments. Submit and Attempt act only on the caller's own submission / attempt (SelfService below).
    /// <summary>Assignments of a ClassCourse (FR-ASG-01, FR-ASG-02).</summary>
    public static class CourseAssignments
    {
        /// <summary>Open assignments; staff also list submissions, students read their own submission and feedback.</summary>
        public const string View = "CourseAssignments.View";

        /// <summary>Create, edit, publish, schedule and delete assignments.</summary>
        public const string Manage = "CourseAssignments.Manage";

        /// <summary>Submit and resubmit one's own work (students).</summary>
        public const string Submit = "CourseAssignments.Submit";

        /// <summary>Grade submissions, write feedback, upload the annotated file and return the work.</summary>
        public const string Grade = "CourseAssignments.Grade";
    }

    /// <summary>Quizzes of a ClassCourse (FR-QZ-01..03).</summary>
    public static class Quizzes
    {
        /// <summary>Open quizzes; staff also list attempts, students read their own attempts and results.</summary>
        public const string View = "Quizzes.View";

        /// <summary>Build quizzes: settings, questions, options, pool; preview as a student.</summary>
        public const string Manage = "Quizzes.Manage";

        /// <summary>Start, autosave and submit one's own attempts (students).</summary>
        public const string Attempt = "Quizzes.Attempt";

        /// <summary>Grade essay answers by hand.</summary>
        public const string Grade = "Quizzes.Grade";
    }

    /// <summary>The digital library of a unit (FR-LIB-01, D13). Unit-scoped, not restricted by a relationship.</summary>
    public static class Library
    {
        /// <summary>Search and read resources of the unit and the units above it (students: matching levels only).</summary>
        public const string View = "Library.View";

        /// <summary>Create, edit and delete resources owned by the unit.</summary>
        public const string Manage = "Library.Manage";
    }

    /// <summary>The forum and announcements of a ClassCourse (FR-FOR-01).</summary>
    public static class Forums
    {
        /// <summary>Read threads, posts and announcements.</summary>
        public const string View = "Forums.View";

        /// <summary>Start threads, reply, and edit or delete one's own posts.</summary>
        public const string Post = "Forums.Post";

        /// <summary>Delete any post or thread, lock and pin threads, write course announcements.</summary>
        public const string Moderate = "Forums.Moderate";
    }

    /// <summary>SCORM and H5P packages of a ClassCourse (FR-STD-01, FR-STD-02, D51, D56).</summary>
    public static class Packages
    {
        /// <summary>Upload, validate, attach to a lesson, read the status of and delete packages; the staff side.</summary>
        public const string Upload = "Packages.Upload";

        /// <summary>Students launch packages of their own ClassCourses and send tracking; staff preview them (no attempt is recorded).</summary>
        public const string View = "Packages.View";
    }

    // M5 wave 1 (M5 plan Task 1). Teacher: own ClassCourses with an assignment active today (L15); Supervisor: supervised courses;
    // Student and Guardian: only their own data (ViewOwn is a SelfService permission), published items only (L6).
    /// <summary>Grading: scales, weight templates, the gradebook, publishing, grade reads and reports (FR-GRD-01..05, D58, D59).</summary>
    public static class Grades
    {
        /// <summary>Read scales, weight templates and gradebooks (staff).</summary>
        public const string View = "Grades.View";

        /// <summary>Configure a gradebook, edit categories, items and entries, import and export columns, preview what a student sees.</summary>
        public const string Manage = "Grades.Manage";

        /// <summary>Publish, schedule and hide grade items for students and guardians.</summary>
        public const string Publish = "Grades.Publish";

        /// <summary>Create and edit grading scales and weight templates of a unit (Org Admin and Manager).</summary>
        public const string ConfigureScales = "Grades.ConfigureScales";

        /// <summary>Student, class and course grade reports and their Excel export.</summary>
        public const string ViewReports = "Grades.ViewReports";

        /// <summary>A student's own published grades, a guardian's children's published grades.</summary>
        public const string ViewOwn = "Grades.ViewOwn";

        /// <summary>M7 wave 1 (FR-GRD-06): ask for a change to a grade of a closed year (a teacher of the own ClassCourse).</summary>
        public const string ChangeRequest = "Grades.ChangeRequest";

        /// <summary>M7 wave 1 (FR-GRD-06, D80): approve or reject a grade-change request; a Manager or the Org Admin, never the requester.</summary>
        public const string ChangeApprove = "Grades.ChangeApprove";
    }

    // M7 wave 1 (FE proposal #808): the promotion run of a year end (FR-STU-07, D12, D78).
    /// <summary>Promotion run: create, preview, edit decisions, execute and undo (Org Admin anywhere, Manager inside their own units).</summary>
    public static class Promotion
    {
        public const string Run = "Promotion.Run";
    }

    // M4b Live sessions, wave 3a (M4b plan Task 1). Teacher: own ClassCourses; Student: own enrollments, and only the
    // sessions whose module the student can see (IModuleAccess). The Zoom webhook is anonymous and needs none.
    /// <summary>Live (Zoom) sessions of a ClassCourse (FR-VC-01).</summary>
    public static class LiveSessions
    {
        /// <summary>See sessions, the upcoming list and join inside the join window (staff with Manage get the start link).</summary>
        public const string View = "LiveSessions.View";

        /// <summary>Schedule, edit, cancel and delete sessions, attach a recording link, read participants and provider status.</summary>
        public const string Manage = "LiveSessions.Manage";
    }

    // Immutable list (not a FrozenSet) so existing consumers typed IReadOnlyList<string> keep compiling;
    // ImmutableArray rejects every mutation through IList<string>/ICollection<string>.
    private static readonly Lazy<IReadOnlyList<string>> AllLazy = new(() =>
        typeof(Permissions)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToImmutableArray());

    /// <summary>Every permission in the catalog (reflection, cached, immutable).</summary>
    public static IReadOnlyList<string> All => AllLazy.Value;

    /// <summary>
    /// Permissions reserved for the Platform Admin (above all clients). No built-in or custom
    /// client role may hold them (enforced by contract tests and by the roles API).
    /// </summary>
    public static IReadOnlyList<string> PlatformOnly { get; } =
        ImmutableArray.Create(Clients.View, Clients.Create, Clients.Update);

    /// <summary>
    /// Permissions that reach only the holder's own data (own student profile, own children, own submission, own quiz
    /// attempt). No staff role holds them, so the escalation guards (D16) count every caller as holding them: handing
    /// them out grants no power over others.
    /// </summary>
    public static IReadOnlyList<string> SelfService { get; } =
        ImmutableArray.Create(Students.SelfEdit, Students.ViewChildren, CourseAssignments.Submit, Quizzes.Attempt, Grades.ViewOwn);

    /// <summary>The caller's permissions plus <see cref="SelfService"/>, for the escalation guards.</summary>
    public static IReadOnlySet<string> WithSelfService(IEnumerable<string> callerPermissions) =>
        callerPermissions.Concat(SelfService).ToHashSet(StringComparer.Ordinal);
}
