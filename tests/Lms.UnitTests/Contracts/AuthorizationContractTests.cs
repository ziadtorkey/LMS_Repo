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

        Assert.Equal("Dean", BuiltInRoles.Get(BuiltInRoles.Manager).UniversityLabel.NameEn);
    }

    [Fact]
    public void OrgAdmin_has_an_explicit_list_not_the_live_catalog()
    {
        // Bug #21: new permissions must not flow to OrgAdmin automatically.
        var orgAdmin = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions;
        Assert.NotSame(Permissions.All, orgAdmin);
        string[] expected =
        [
            "OrgUnits.View", "OrgUnits.Create", "OrgUnits.Update", "OrgUnits.Delete",
            "Roles.View", "Roles.Create", "Roles.Update", "Roles.Delete",
            "Assignments.View", "Assignments.Create", "Assignments.Delete",
            "Settings.View", "Settings.Update", "Policies.View", "Policies.Update",
            "Audit.View", "Audit.Export", "Files.Upload", "Files.Download",
        ];
        Assert.Equal(expected.Order(), orgAdmin.Order());
    }

    [Fact]
    public void No_built_in_role_holds_a_platform_only_permission()
    {
        Assert.All(Permissions.PlatformOnly, p => Assert.Contains(p, Permissions.All));
        Assert.All(BuiltInRoles.All, r =>
            Assert.Empty(r.DefaultPermissions.Intersect(Permissions.PlatformOnly)));
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
