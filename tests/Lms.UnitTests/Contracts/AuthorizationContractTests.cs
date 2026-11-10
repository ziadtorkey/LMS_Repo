using System.Text.RegularExpressions;
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
    public void Permission_catalog_contains_the_M0_set()
    {
        string[] expected =
        [
            "OrgUnits.View", "OrgUnits.Create", "OrgUnits.Update", "OrgUnits.Delete",
            "Roles.View", "Roles.Create", "Roles.Update", "Roles.Delete",
            "Assignments.View", "Assignments.Create", "Assignments.Delete",
            "Settings.View", "Settings.Update", "Policies.View", "Policies.Update",
            "Audit.View", "Audit.Export", "Files.Upload", "Files.Download",
        ];
        Assert.Equal(expected.Order(), Permissions.All.Order());
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

        Assert.Equal(Permissions.All, BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions);
        Assert.Equal("Dean", BuiltInRoles.Get(BuiltInRoles.Manager).UniversityLabel.NameEn);
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
