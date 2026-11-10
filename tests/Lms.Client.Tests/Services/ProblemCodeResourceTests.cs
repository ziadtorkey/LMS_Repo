using System.Globalization;
using Bunit;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

public sealed class ProblemCodeResourceTests : ClientTestContext
{
    // Literal list from the M1 plan (Task 1). Replace with reflection over Lms.Shared.ProblemCodes once that contract merges.
    private static readonly string[] Codes =
    [
        "not_found", "validation_failed", "code_taken", "name_taken", "level_type_in_use", "invalid_level_type",
        "unit_has_children", "unit_has_assignments", "unit_in_use", "root_unit", "invalid_move", "invalid_transfer_target",
        "transfer_target_in_use", "setting_locked", "policy_inherited", "built_in_role", "role_in_use",
        "platform_only_permission", "permission_escalation", "last_admin_permission", "assignment_exists", "own_assignment",
        "branch_too_large", "placeholder_missing", "placeholder_unknown", "export_too_large", "year_overlap", "year_closed",
        "year_active", "active_year_exists", "term_overlap", "term_outside_year", "event_outside_year", "in_app_required",
    ];

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_problem_code_has_an_error_resource(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var missing = Codes.Where(c => l[$"Error_{c}"].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }
}
