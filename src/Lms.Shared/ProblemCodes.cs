namespace Lms.Shared;

/// <summary>
/// Values of the ProblemDetails extension <c>code</c> (M1 Global Constraints). Each value equals its name;
/// the client shows the resource <c>Error_{code}</c>. Append only; never rename.
/// </summary>
public static class ProblemCodes
{
    public const string not_found = nameof(not_found);
    public const string validation_failed = nameof(validation_failed);
    public const string code_taken = nameof(code_taken);
    public const string name_taken = nameof(name_taken);
    public const string level_type_in_use = nameof(level_type_in_use);
    public const string invalid_level_type = nameof(invalid_level_type);
    public const string unit_has_children = nameof(unit_has_children);
    public const string unit_has_assignments = nameof(unit_has_assignments);
    public const string unit_in_use = nameof(unit_in_use);
    public const string root_unit = nameof(root_unit);
    public const string invalid_move = nameof(invalid_move);
    public const string invalid_transfer_target = nameof(invalid_transfer_target);
    public const string transfer_target_in_use = nameof(transfer_target_in_use);
    public const string setting_locked = nameof(setting_locked);
    public const string policy_inherited = nameof(policy_inherited);
    public const string built_in_role = nameof(built_in_role);
    public const string role_in_use = nameof(role_in_use);
    public const string platform_only_permission = nameof(platform_only_permission);
    public const string permission_escalation = nameof(permission_escalation);
    public const string last_admin_permission = nameof(last_admin_permission);
    public const string assignment_exists = nameof(assignment_exists);
    public const string own_assignment = nameof(own_assignment);
    public const string branch_too_large = nameof(branch_too_large);
    public const string placeholder_missing = nameof(placeholder_missing);
    public const string placeholder_unknown = nameof(placeholder_unknown);
    public const string export_too_large = nameof(export_too_large);
    public const string year_overlap = nameof(year_overlap);
    public const string year_closed = nameof(year_closed);
    public const string year_active = nameof(year_active);
    public const string active_year_exists = nameof(active_year_exists);
    public const string term_overlap = nameof(term_overlap);
    public const string term_outside_year = nameof(term_outside_year);
    public const string event_outside_year = nameof(event_outside_year);
    public const string in_app_required = nameof(in_app_required);

    // M2 people: staff (D23, D26). 409 duplicate_person carries extension `field` (email | mobile | nationalId) only.
    public const string duplicate_person = nameof(duplicate_person);
    public const string person_not_found = nameof(person_not_found);
    public const string invalid_role_kind = nameof(invalid_role_kind);
    public const string cannot_change_own_status = nameof(cannot_change_own_status);
    public const string already_activated = nameof(already_activated);
    public const string invalid_expiry = nameof(invalid_expiry);

    // M2 imports (X-06, D34)
    public const string import_file_invalid = nameof(import_file_invalid);
    public const string import_too_many_rows = nameof(import_too_many_rows);
    public const string import_not_valid = nameof(import_not_valid);
    public const string import_in_progress = nameof(import_in_progress);
    public const string import_already_committed = nameof(import_already_committed);

    // M2 external sign-in (FR-SEC-08, D33)
    public const string provider_slug_taken = nameof(provider_slug_taken);
    public const string provider_unreachable = nameof(provider_unreachable);
    public const string external_account_not_found = nameof(external_account_not_found);
    public const string external_account_ambiguous = nameof(external_account_ambiguous);

    // M3 students, guardians and change requests (M2 appendix, D24, D25). invalid_level_type and year_closed exist above.
    public const string possible_duplicate = nameof(possible_duplicate);
    public const string student_code_taken = nameof(student_code_taken);
    public const string student_code_required = nameof(student_code_required);
    public const string student_code_not_allowed = nameof(student_code_not_allowed);
    public const string student_code_exhausted = nameof(student_code_exhausted);
    public const string invalid_code_pattern = nameof(invalid_code_pattern);
    public const string guardian_required = nameof(guardian_required);
    public const string primary_guardian_required = nameof(primary_guardian_required);
    public const string guardian_link_exists = nameof(guardian_link_exists);
    public const string guardian_incomplete = nameof(guardian_incomplete);
    public const string critical_field = nameof(critical_field);
    public const string change_request_pending = nameof(change_request_pending);
    public const string change_request_closed = nameof(change_request_closed);
    public const string change_request_stale = nameof(change_request_stale);
    public const string password_change_required = nameof(password_change_required);

    // M3 academic structure (courses, blueprints, classes, teachers, enrollment, groups, transfers)
    public const string invalid_owner_unit = nameof(invalid_owner_unit);
    public const string course_code_taken = nameof(course_code_taken);
    public const string course_in_use = nameof(course_in_use);
    public const string course_not_available = nameof(course_not_available);
    public const string allocation_exists = nameof(allocation_exists);
    public const string allocation_in_use = nameof(allocation_in_use);
    public const string restriction_not_met = nameof(restriction_not_met);
    public const string exception_pending = nameof(exception_pending);
    public const string exception_closed = nameof(exception_closed);
    public const string blueprint_draft_exists = nameof(blueprint_draft_exists);
    public const string blueprint_no_draft = nameof(blueprint_no_draft);
    public const string blueprint_stale = nameof(blueprint_stale);
    public const string blueprint_empty = nameof(blueprint_empty);
    public const string class_code_taken = nameof(class_code_taken);
    public const string class_in_use = nameof(class_in_use);
    public const string class_closed = nameof(class_closed);
    public const string class_full = nameof(class_full);
    public const string over_capacity = nameof(over_capacity);
    public const string year_not_effective = nameof(year_not_effective);
    public const string class_course_exists = nameof(class_course_exists);
    public const string class_course_in_use = nameof(class_course_in_use);
    public const string enrollment_not_optional = nameof(enrollment_not_optional);
    public const string already_enrolled = nameof(already_enrolled);
    public const string not_in_class = nameof(not_in_class);
    public const string already_placed = nameof(already_placed);
    public const string class_unit_mismatch = nameof(class_unit_mismatch);
    public const string teacher_not_eligible = nameof(teacher_not_eligible);
    public const string teacher_not_in_unit = nameof(teacher_not_in_unit);
    public const string primary_teacher_exists = nameof(primary_teacher_exists);
    public const string already_in_group = nameof(already_in_group);
    public const string not_enrolled = nameof(not_enrolled);
    public const string transfer_invalid = nameof(transfer_invalid);
    public const string too_many_items = nameof(too_many_items);
    public const string short_code_taken = nameof(short_code_taken);
    public const string invalid_short_code = nameof(invalid_short_code);
    public const string teacher_already_assigned = nameof(teacher_already_assigned);
    public const string assignment_ended = nameof(assignment_ended);

    // M4a wave 1 (lesson modules, assets, completion; M4a plan Task 1)
    /// <summary>Scheduled visibility needs a from and/or to date, and from must be before to.</summary>
    public const string module_visibility_range_invalid = nameof(module_visibility_range_invalid);

    /// <summary>A group target must name a live group of the module's ClassCourse; a ClassCourse target names none.</summary>
    public const string module_target_invalid = nameof(module_target_invalid);

    /// <summary>The file's extension / content type is not in <c>AssetTypes.All</c>, or does not match the module kind.</summary>
    public const string asset_type_not_allowed = nameof(asset_type_not_allowed);

    /// <summary>The file is larger than the effective <c>Files.MaxUploadMb</c>.</summary>
    public const string asset_too_large = nameof(asset_too_large);

    /// <summary>Only a module with the Manual completion rule can be marked complete by the student.</summary>
    public const string completion_not_manual = nameof(completion_not_manual);
}
