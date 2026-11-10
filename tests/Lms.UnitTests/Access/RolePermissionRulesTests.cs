using Lms.Infrastructure.Access;
using Lms.Shared;
using Lms.Shared.Authorization;

namespace Lms.UnitTests.Access;

public sealed class RolePermissionRulesTests
{
    private static readonly IReadOnlySet<string> Admin = Permissions.All
        .Except(Permissions.PlatformOnly).ToHashSet();

    [Fact]
    public void Valid_subset_passes() =>
        Assert.Null(RolePermissionRules.Validate([Permissions.Roles.View], Admin, false, null));

    [Fact]
    public void Unknown_permission_is_validation_failed() =>
        Assert.Equal(ProblemCodes.validation_failed,
            RolePermissionRules.Validate(["Nope.Nothing"], Admin, false, null));

    [Fact]
    public void Platform_only_permission_is_refused_even_for_platform_admin()
    {
        Assert.Equal(ProblemCodes.platform_only_permission,
            RolePermissionRules.Validate([Permissions.Clients.Create], Admin, false, null));
        Assert.Equal(ProblemCodes.platform_only_permission,
            RolePermissionRules.Validate([Permissions.Clients.Create], Admin, true, null));
    }

    [Fact]
    public void Permission_the_caller_lacks_is_escalation()
    {
        var caller = new HashSet<string> { Permissions.Roles.View };
        Assert.Equal(ProblemCodes.permission_escalation,
            RolePermissionRules.Validate([Permissions.Roles.View, Permissions.Audit.View], caller, false, null));
    }

    [Fact]
    public void Platform_admin_may_grant_any_client_permission() =>
        Assert.Null(RolePermissionRules.Validate([Permissions.Audit.View], new HashSet<string>(), true, null));

    [Theory]
    [InlineData("Roles.View")]
    [InlineData("Roles.Update")]
    public void OrgAdmin_cannot_lose_roles_view_or_update(string kept)
    {
        var other = kept == Permissions.Roles.View ? Permissions.Roles.Update : Permissions.Roles.View;
        Assert.Equal(ProblemCodes.last_admin_permission,
            RolePermissionRules.Validate([kept, Permissions.Audit.View], Admin, false, BuiltInRoles.OrgAdmin));
        Assert.Null(RolePermissionRules.Validate([kept, other], Admin, false, BuiltInRoles.OrgAdmin));
    }

    [Fact]
    public void Other_built_in_roles_may_lose_roles_permissions() =>
        Assert.Null(RolePermissionRules.Validate([Permissions.Audit.View], Admin, false, BuiltInRoles.Teacher));
}
