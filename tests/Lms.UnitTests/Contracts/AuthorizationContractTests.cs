using System.Collections.Frozen;
using System.Reflection;
using System.Text.RegularExpressions;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;

namespace Lms.UnitTests.Contracts;

public partial class AuthorizationContractTests
{
    [GeneratedRegex("^[A-Z][A-Za-z]+\\.[A-Z][A-Za-z]+$")]
    private static partial Regex ModuleActionFormat();

    [Fact]
    public void Every_permission_constant_matches_Module_Action_format_and_is_unique()
    {
        Assert.NotEmpty(Permissions.All);
        Assert.All(Permissions.All, p => Assert.Matches(ModuleActionFormat(), p));
        Assert.Equal(Permissions.All.Count, Permissions.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Permission_catalog_contains_exactly_the_M0_to_M6_wave_1_set()
    {
        string[] expected =
        [
            "OrgUnits.View", "OrgUnits.Create", "OrgUnits.Update", "OrgUnits.Delete", "OrgUnits.Move",
            "Roles.View", "Roles.Create", "Roles.Update", "Roles.Delete",
            "Assignments.View", "Assignments.Create", "Assignments.Update", "Assignments.Delete",
            "Settings.View", "Settings.Update", "Policies.View", "Policies.Update",
            "Audit.View", "Audit.Export", "Files.Upload", "Files.Download",
            "Clients.View", "Clients.Create", "Clients.Update",
            "LevelTypes.View", "LevelTypes.Create", "LevelTypes.Update", "LevelTypes.Delete",
            "Templates.View", "Templates.Create", "Templates.Delete", "Templates.Apply",
            "AcademicYears.View", "AcademicYears.Create", "AcademicYears.Update", "AcademicYears.Delete",
            "Staff.View", "Staff.Create", "Staff.Update", "Staff.Delete", "Staff.Import",
            "IdentityProviders.View", "IdentityProviders.Manage",
            "Students.View", "Students.Create", "Students.Update", "Students.Delete", "Students.Import",
            "Students.ApproveChanges", "Students.SelfEdit", "Students.ViewChildren",
            "Courses.View", "Courses.Create", "Courses.Update", "Courses.Delete", "Courses.Import", "Courses.Allocate",
            "Courses.ManageSupervisors",
            "Blueprints.View", "Blueprints.Edit", "Blueprints.Publish",
            "Classes.View", "Classes.Create", "Classes.Update", "Classes.Delete", "Classes.Close", "Classes.ManageCourses",
            "TeacherAssignments.View", "TeacherAssignments.Manage",
            "Enrollment.View", "Enrollment.Manage", "Enrollment.Import", "Enrollment.Transfer",
            "Enrollment.RequestException", "Enrollment.ApproveException",
            "Groups.View", "Groups.Manage", "LessonPlans.View",
            "Modules.View", "Modules.Manage",
            "CourseAssignments.View", "CourseAssignments.Manage", "CourseAssignments.Submit", "CourseAssignments.Grade",
            "Quizzes.View", "Quizzes.Manage", "Quizzes.Attempt", "Quizzes.Grade",
            "Library.View", "Library.Manage",
            "Forums.View", "Forums.Post", "Forums.Moderate",
            "LiveSessions.View", "LiveSessions.Manage",
            "Packages.Upload", "Packages.View",
            "Grades.View", "Grades.Manage", "Grades.Publish", "Grades.ConfigureScales", "Grades.ViewReports", "Grades.ViewOwn",
            // M5 wave 2 (timetable)
            "Timetable.View", "Timetable.Manage", "Timetable.Configure", "Timetable.ViewOwn",
            "Attendance.Take", "Attendance.Edit", "Attendance.View", "Attendance.ViewOwn",
            "Excuses.Submit", "Excuses.Review", "Excuses.Escalate",
            "Messaging.View", "Messaging.Send", "Messaging.Moderate", "Announcements.View", "Announcements.Manage", "Comms.Manage",
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), Permissions.All.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Permissions_All_is_immutable()
    {
        Assert.IsNotType<string[]>(Permissions.All);
        Assert.IsNotType<List<string>>(Permissions.All);
        var asList = Assert.IsAssignableFrom<IList<string>>(Permissions.All);
        Assert.True(asList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => asList.Add("Hacked.Permission"));
        Assert.Throws<NotSupportedException>(() => asList[0] = "Hacked.Permission");
        Assert.Same(Permissions.All, Permissions.All);
    }

    [Fact]
    public void Policy_catalog_is_immutable()
    {
        Assert.IsAssignableFrom<FrozenDictionary<string, IReadOnlyList<string>>>(PolicyCatalog.BlockedPermissions);
        Assert.All(PolicyCatalog.BlockedPermissions.Values, v =>
        {
            var asList = Assert.IsAssignableFrom<IList<string>>(v);
            Assert.True(asList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => asList.Add("Hacked.Permission"));
        });
    }

    [Fact]
    public void ProblemCodes_values_equal_their_names_and_are_unique()
    {
        var fields = typeof(ProblemCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToList();
        string[] expected =
        [
            "not_found", "validation_failed", "code_taken", "name_taken", "level_type_in_use", "invalid_level_type",
            "unit_has_children", "unit_has_assignments", "unit_in_use", "root_unit", "invalid_move",
            "invalid_transfer_target", "transfer_target_in_use", "setting_locked", "policy_inherited", "built_in_role",
            "role_in_use", "platform_only_permission", "permission_escalation", "last_admin_permission",
            "assignment_exists", "own_assignment", "branch_too_large", "placeholder_missing", "placeholder_unknown",
            "export_too_large", "year_overlap", "year_closed", "year_active", "active_year_exists", "term_overlap",
            "term_outside_year", "event_outside_year", "in_app_required",
            // M2 (staff, imports, identity providers)
            "duplicate_person", "person_not_found", "invalid_role_kind", "cannot_change_own_status", "already_activated",
            "invalid_expiry", "import_file_invalid", "import_too_many_rows", "import_not_valid", "import_in_progress",
            "import_already_committed", "provider_slug_taken", "provider_unreachable", "external_account_not_found",
            "external_account_ambiguous",
            // M3 (students, guardians, change requests; academic structure)
            "possible_duplicate", "student_code_taken", "student_code_required", "student_code_not_allowed",
            "student_code_exhausted", "invalid_code_pattern", "guardian_required", "primary_guardian_required",
            "guardian_link_exists", "guardian_incomplete", "critical_field", "change_request_pending",
            "change_request_closed", "change_request_stale", "password_change_required",
            "invalid_owner_unit", "course_code_taken", "course_in_use", "course_not_available", "allocation_exists",
            "allocation_in_use", "restriction_not_met", "exception_pending", "exception_closed", "blueprint_draft_exists",
            "blueprint_no_draft", "blueprint_stale", "blueprint_empty", "class_code_taken", "class_in_use", "class_closed",
            "class_full", "over_capacity", "year_not_effective", "class_course_exists", "class_course_in_use",
            "enrollment_not_optional", "already_enrolled", "not_in_class", "already_placed", "class_unit_mismatch",
            "teacher_not_eligible", "teacher_not_in_unit", "primary_teacher_exists", "already_in_group", "not_enrolled",
            "transfer_invalid", "too_many_items", "short_code_taken", "invalid_short_code", "teacher_already_assigned", "assignment_ended",
            // M4a wave 1 (lesson modules, assets, completion)
            "module_visibility_range_invalid", "module_target_invalid", "asset_type_not_allowed", "asset_too_large",
            "completion_not_manual",
            // M4a wave 2 (assignments, quizzes, library, forums)
            "late_policy_invalid", "submission_closed", "resubmission_not_allowed", "quiz_closed", "quiz_not_open",
            "attempt_limit_reached", "attempt_expired", "quiz_has_attempts", "pool_draw_invalid", "thread_locked",
            "download_disabled",
            // M4b wave 3a (live sessions)
            "provider_unavailable", "provider_not_configured", "live_session_not_open", "feature_disabled",
            "recurrence_invalid",
            // M4b wave 3b (SCORM and H5P packages)
            "package_invalid", "package_too_large", "package_not_ready", "package_not_attached", "package_commit_rejected",
            "package_host_not_configured",
            // M5 wave 1 (grading)
            "gradebook_locked", "scale_provisional", "scale_bands_invalid", "scale_in_use", "weights_invalid", "grade_out_of_range",
            "grade_override_reason_required", "grade_import_invalid", "grade_import_expired", "terms_have_dependants",
            "concurrency_conflict",
            // M5 wave 2 (timetable)
            "timetable_conflict", "period_overlap", "period_in_use", "timetable_not_published", "exception_date_invalid", "exception_substitute_invalid",
            // M5 wave 3 (attendance and excuses)
            "attendance_edit_window_closed", "attendance_session_future", "attendance_unmarked_rows", "attendance_reason_required",
            "attendance_not_a_proposal", "excuse_out_of_policy", "excuse_submitter_not_allowed", "excuse_reviewer_is_submitter",
            "excuse_comment_required", "excuse_attachment_invalid", "excuse_not_pending",
            // M6 wave 1 (messaging, announcements, delivery)
            "messaging_not_allowed", "conversation_closed", "message_too_long", "message_delete_window_closed", "report_duplicate", "rate_limited",
            "notification_mandatory", "announcement_target_invalid", "announcement_not_editable", "rules_looser_than_default",
        ];

        Assert.All(fields, f => Assert.Equal(f.Name, (string)f.GetRawConstantValue()!));
        var values = fields.Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(expected.Order(StringComparer.Ordinal), values.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_policy_entry_references_existing_permissions()
    {
        Assert.NotEmpty(PolicyCatalog.BlockedPermissions);
        Assert.All(PolicyCatalog.BlockedPermissions.Values.SelectMany(v => v),
            p => Assert.Contains(p, Permissions.All));
    }

    [Fact]
    public void Seven_built_in_roles_with_valid_permissions_and_bilingual_labels()
    {
        Assert.Equal(
            ["OrgAdmin", "Manager", "Supervisor", "Counselor", "Teacher", "Student", "Guardian"],
            BuiltInRoles.All.Select(r => r.Key));

        Assert.All(BuiltInRoles.All, r =>
        {
            Assert.All(r.DefaultPermissions, p => Assert.Contains(p, Permissions.All));
            Assert.Contains(r.BaseKind, new[] { RoleBaseKinds.Staff, RoleBaseKinds.Student, RoleBaseKinds.Guardian });
            foreach (var label in new[] { r.SchoolGroupLabel, r.UniversityLabel })
            {
                Assert.False(string.IsNullOrWhiteSpace(label.NameAr));
                Assert.False(string.IsNullOrWhiteSpace(label.NameEn));
            }
        });

        Assert.Equal("Dean", BuiltInRoles.Get(BuiltInRoles.Manager).UniversityLabel.NameEn);
    }

    [Fact]
    public void OrgAdmin_has_an_explicit_list_not_the_live_catalog()
    {
        // Bug #21: new permissions must not flow to OrgAdmin automatically (exact list pinned in BuiltInRoleMatrixTests).
        var orgAdmin = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions;
        Assert.NotSame(Permissions.All, orgAdmin);
        Assert.NotEqual(Permissions.All.Order(StringComparer.Ordinal), orgAdmin.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(Permissions.Clients.Create, orgAdmin);
    }

    [Fact]
    public void Built_in_roles_do_not_share_permission_list_instances()
    {
        var lists = BuiltInRoles.All.Select(r => r.DefaultPermissions).ToList();
        Assert.Equal(lists.Count, lists.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public void OrgAdmin_arabic_label_is_distinct_from_manager_label()
    {
        var orgAdmin = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).SchoolGroupLabel.NameAr;
        Assert.Equal("مسؤول النظام", orgAdmin);
        Assert.NotEqual(BuiltInRoles.Get(BuiltInRoles.Manager).SchoolGroupLabel.NameAr, orgAdmin);
    }

    [Fact]
    public void Teacher_cannot_see_roles_or_audit_by_default()
    {
        var teacher = BuiltInRoles.Get(BuiltInRoles.Teacher).DefaultPermissions;
        Assert.DoesNotContain(Permissions.Roles.View, teacher);
        Assert.DoesNotContain(Permissions.Audit.View, teacher);
    }

    [Fact]
    public void Unknown_built_in_role_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BuiltInRoles.Get("Nope"));
    }

    [Fact]
    public void Context_header_and_block_messages_match_plan()
    {
        Assert.Equal("X-Lms-Context", ContextHeader.Name);
        Assert.Equal("Blocked by parent organization policy", PolicyCatalog.BlockedMessageEn);
        Assert.Equal("محظور بسياسة الوحدة الأعلى", PolicyCatalog.BlockedMessageAr);
    }

    [Fact]
    public void M0_setting_keys_have_plan_defaults()
    {
        Assert.Equal(("Ui.DefaultCulture", "ar"), (SettingKeys.UiDefaultCulture.Name, SettingKeys.UiDefaultCulture.Default));
        Assert.Equal(("Files.MaxUploadMb", 200), (SettingKeys.FilesMaxUploadMb.Name, SettingKeys.FilesMaxUploadMb.Default));
    }
}
