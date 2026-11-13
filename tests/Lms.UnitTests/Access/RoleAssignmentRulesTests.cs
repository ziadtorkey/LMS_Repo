using Lms.Infrastructure.Access;
using Lms.Shared.Authorization;

namespace Lms.UnitTests.Access;

public sealed class RoleAssignmentRulesTests
{
    private static HashSet<string> Of(string role) =>
        BuiltInRoles.All.Single(r => r.Key == role).DefaultPermissions.ToHashSet();

    private static bool CanAssign(string callerRole, string targetRole) =>
        RoleAssignmentRules.CanAssign(Of(targetRole), Of(callerRole), callerIsPlatformAdmin: false);

    [Theory]
    [InlineData(BuiltInRoles.Teacher)]
    [InlineData(BuiltInRoles.Counselor)]
    [InlineData(BuiltInRoles.Supervisor)]
    public void Manager_can_assign_roles_that_are_a_strict_subset_of_their_own(string target) =>
        Assert.True(CanAssign(BuiltInRoles.Manager, target));

    [Theory]
    [InlineData(BuiltInRoles.Manager)]
    [InlineData(BuiltInRoles.OrgAdmin)]
    public void Manager_cannot_assign_Manager_or_OrgAdmin(string target) =>
        Assert.False(CanAssign(BuiltInRoles.Manager, target));

    [Fact]
    public void OrgAdmin_can_assign_OrgAdmin_because_they_hold_every_assignable_permission() =>
        Assert.True(CanAssign(BuiltInRoles.OrgAdmin, BuiltInRoles.OrgAdmin));

    [Fact]
    public void Platform_admin_is_exempt() =>
        Assert.True(RoleAssignmentRules.CanAssign(Of(BuiltInRoles.OrgAdmin), new HashSet<string>(), callerIsPlatformAdmin: true));

    [Fact]
    public void A_role_with_a_permission_the_caller_lacks_is_refused() =>
        Assert.False(RoleAssignmentRules.CanAssign(
            new HashSet<string> { Permissions.Audit.View }, new HashSet<string> { Permissions.Roles.View }, false));

    [Theory]
    [InlineData(BuiltInRoles.Student)]
    [InlineData(BuiltInRoles.Guardian)]
    [InlineData(BuiltInRoles.Teacher)]
    public void OrgAdmin_can_assign_roles_holding_self_service_permissions_nobody_on_staff_holds(string target) =>
        Assert.True(CanAssign(BuiltInRoles.OrgAdmin, target));

    [Theory]
    [InlineData(BuiltInRoles.Student)]
    [InlineData(BuiltInRoles.Guardian)]
    public void Manager_can_assign_Student_and_Guardian(string target) =>
        Assert.True(CanAssign(BuiltInRoles.Manager, target));

    [Fact]
    public void Self_service_permissions_are_only_the_own_data_ones() =>
        Assert.Equal(["CourseAssignments.Submit", "Quizzes.Attempt", "Students.SelfEdit", "Students.ViewChildren"],
            Permissions.SelfService.Order(StringComparer.Ordinal));

    [Theory]
    [InlineData(BuiltInRoles.Manager, BuiltInRoles.Teacher)]
    [InlineData(BuiltInRoles.Manager, BuiltInRoles.Student)]
    [InlineData(BuiltInRoles.Manager, BuiltInRoles.Supervisor)]
    [InlineData(BuiltInRoles.OrgAdmin, BuiltInRoles.Student)]
    [InlineData(BuiltInRoles.OrgAdmin, BuiltInRoles.OrgAdmin)]
    public void M4a_wave_2_defaults_keep_the_escalation_guard_assignable(string caller, string target) =>
        Assert.True(CanAssign(caller, target));
}
