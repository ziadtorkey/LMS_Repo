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
}
