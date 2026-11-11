using System.Reflection;
using System.Text.Json;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Organizations;

namespace Lms.UnitTests.Contracts;

public class ConfigurationContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Every public static SettingKey&lt;T&gt; field of SettingKeys and FeatureFlags: (name, default, CLR type).</summary>
    public static IEnumerable<object[]> DeclaredKeys() =>
        new[] { typeof(SettingKeys), typeof(FeatureFlags) }
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(SettingKey<>))
            .Select(f =>
            {
                var key = f.GetValue(null)!;
                var name = (string)f.FieldType.GetProperty(nameof(SettingKey<int>.Name))!.GetValue(key)!;
                var value = f.FieldType.GetProperty(nameof(SettingKey<int>.Default))!.GetValue(key)!;
                return new object[] { name, value, f.FieldType.GetGenericArguments()[0] };
            });

    [Fact]
    public void Declared_keys_are_found()
    {
        Assert.Equal(9, DeclaredKeys().Count());
    }

    [Theory]
    [MemberData(nameof(DeclaredKeys))]
    public void Every_SettingKeys_and_FeatureFlags_field_is_in_the_catalog_with_matching_default_json(
        string name, object defaultValue, Type valueType)
    {
        var definition = SettingCatalog.Find(name);

        Assert.NotNull(definition);
        Assert.Equal(JsonSerializer.Serialize(defaultValue, valueType), definition.DefaultJson);
        var expectedKinds = valueType == typeof(bool) ? new[] { SettingValueKinds.Bool }
            : valueType == typeof(int) ? [SettingValueKinds.Int]
            : [SettingValueKinds.String, SettingValueKinds.Enum];
        Assert.Contains(definition.Kind, expectedKinds);
    }

    [Fact]
    public void Catalog_names_are_unique()
    {
        Assert.Equal(SettingCatalog.All.Count, SettingCatalog.All.Select(d => d.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(DeclaredKeys().Count(), SettingCatalog.All.Count);
    }

    [Fact]
    public void Catalog_matches_plan()
    {
        Assert.Equal(
        [
            "Ui.DefaultCulture", "Ui.ShowHijriDates", "Files.MaxUploadMb",
            "Features.VirtualClasses", "Features.DigitalLibrary", "Features.ScormContent", "Features.Messaging",
            "Features.GuardianPortal", "Staff.ExpiryNoticeDays",
        ], SettingCatalog.All.Select(d => d.Name));

        var culture = SettingCatalog.Find("Ui.DefaultCulture")!;
        Assert.Equal((SettingValueKinds.Enum, SettingCategories.General, "\"ar\""), (culture.Kind, culture.Category, culture.DefaultJson));
        Assert.Equal(["ar", "en"], culture.AllowedValues!);

        var hijri = SettingCatalog.Find("Ui.ShowHijriDates")!;
        Assert.Equal((SettingValueKinds.Bool, SettingCategories.General, "false"), (hijri.Kind, hijri.Category, hijri.DefaultJson));

        var upload = SettingCatalog.Find("Files.MaxUploadMb")!;
        Assert.Equal((SettingValueKinds.Int, SettingCategories.Files, "200", 1, 2048),
            (upload.Kind, upload.Category, upload.DefaultJson, upload.Min, upload.Max));

        Assert.All(FeatureFlags.All, f =>
        {
            var d = SettingCatalog.Find(f.Name)!;
            Assert.StartsWith("Features.", f.Name, StringComparison.Ordinal);
            Assert.False(f.Default);
            Assert.Equal((SettingValueKinds.Bool, SettingCategories.Features, "false"), (d.Kind, d.Category, d.DefaultJson));
        });
        Assert.Equal(5, FeatureFlags.All.Count);

        var expiry = SettingCatalog.Find("Staff.ExpiryNoticeDays")!;
        Assert.Equal((SettingValueKinds.Int, SettingCategories.People, "14", 0, 90),
            (expiry.Kind, expiry.Category, expiry.DefaultJson, expiry.Min, expiry.Max));
        Assert.Equal(("Staff.ExpiryNoticeDays", 14), (SettingKeys.StaffExpiryNoticeDays.Name, SettingKeys.StaffExpiryNoticeDays.Default));
        Assert.Null(SettingCatalog.Find("Nope.Nope"));
    }

    [Fact]
    public void Enum_definitions_list_their_default_among_allowed_values()
    {
        var enums = SettingCatalog.All.Where(d => d.Kind == SettingValueKinds.Enum).ToList();
        Assert.NotEmpty(enums);
        Assert.All(enums, d =>
        {
            Assert.NotNull(d.AllowedValues);
            Assert.Contains(JsonSerializer.Deserialize<string>(d.DefaultJson), d.AllowedValues!);
        });
    }

    [Fact]
    public void Int_definitions_have_their_default_within_range()
    {
        Assert.All(SettingCatalog.All.Where(d => d.Kind == SettingValueKinds.Int), d =>
        {
            var value = JsonSerializer.Deserialize<int>(d.DefaultJson);
            Assert.InRange(value, d.Min ?? int.MinValue, d.Max ?? int.MaxValue);
        });
    }

    [Fact]
    public void Every_policy_key_blocks_a_permission_or_a_role()
    {
        Assert.All(PolicyCatalog.BlockedPermissions, entry =>
        {
            var roles = PolicyCatalog.BlockedRoles.GetValueOrDefault(entry.Key) ?? [];
            Assert.True(entry.Value.Count + roles.Count > 0, $"{entry.Key} blocks nothing.");
            Assert.All(entry.Value, p => Assert.Contains(p, Permissions.All));
        });
        Assert.All(PolicyCatalog.BlockedRoles.Keys, k => Assert.Contains(k, PolicyCatalog.BlockedPermissions.Keys));
    }

    [Fact]
    public void Policy_catalog_matches_the_M2_plan()
    {
        Assert.Equal(
            ["CreateAdmins", "CreateOrgUnits", "CreateStaff", "CreateTeachers"],
            PolicyCatalog.BlockedPermissions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["OrgUnits.Create"], PolicyCatalog.BlockedPermissions[PolicyCatalog.CreateOrgUnits]);
        Assert.Equal(["Staff.Create", "Staff.Import"], PolicyCatalog.BlockedPermissions[PolicyCatalog.CreateStaff]);
        Assert.Empty(PolicyCatalog.BlockedPermissions[PolicyCatalog.CreateTeachers]);
        Assert.Empty(PolicyCatalog.BlockedPermissions[PolicyCatalog.CreateAdmins]);

        Assert.Equal(["CreateAdmins", "CreateTeachers"], PolicyCatalog.BlockedRoles.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["Teacher"], PolicyCatalog.BlockedRoles[PolicyCatalog.CreateTeachers]);
        Assert.Equal(["OrgAdmin", "Manager"], PolicyCatalog.BlockedRoles[PolicyCatalog.CreateAdmins]);
        Assert.Equal(["Assignments.Create", "Roles.Update"], PolicyCatalog.AdminMarkerPermissions);
    }

    [Fact]
    public void Blocked_roles_are_built_in_keys()
    {
        var builtIn = BuiltInRoles.All.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(PolicyCatalog.BlockedRoles);
        Assert.All(PolicyCatalog.BlockedRoles.Values.SelectMany(v => v), r => Assert.Contains(r, builtIn));
        Assert.All(PolicyCatalog.AdminMarkerPermissions, p => Assert.Contains(p, Permissions.All));
    }

    [Fact]
    public void Role_blocking_catalogs_are_immutable()
    {
        Assert.IsAssignableFrom<System.Collections.Frozen.FrozenDictionary<string, IReadOnlyList<string>>>(PolicyCatalog.BlockedRoles);
        Assert.All(PolicyCatalog.BlockedRoles.Values.Append(PolicyCatalog.AdminMarkerPermissions), v =>
        {
            var asList = Assert.IsAssignableFrom<IList<string>>(v);
            Assert.True(asList.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => asList.Add("Hacked"));
        });
    }

    [Fact]
    public void State_and_visibility_constants_match_plan()
    {
        Assert.Equal(("Off", "SetHere", "Inherited"), (PolicyStates.Off, PolicyStates.SetHere, PolicyStates.Inherited));
        Assert.Equal(("Private", "Shared"), (TemplateVisibilities.Private, TemplateVisibilities.Shared));
        Assert.Equal(["Private", "Shared"], TemplateVisibilities.All);
        Assert.Equal(("Bool", "Int", "String", "Enum"),
            (SettingValueKinds.Bool, SettingValueKinds.Int, SettingValueKinds.String, SettingValueKinds.Enum));
        Assert.Equal(("General", "Files", "Features", "People"),
            (SettingCategories.General, SettingCategories.Files, SettingCategories.Features, SettingCategories.People));
    }

    [Fact]
    public void Configuration_routes_match_plan()
    {
        var id = Guid.Parse("0198a6f2-0000-7000-8000-000000000002");

        Assert.Equal("/api/policies/catalog", Routes.PoliciesCatalog);
        Assert.Equal("/api/settings/catalog", Routes.SettingsCatalog);
        Assert.Equal("/api/org-units/copy", Routes.OrgUnitsCopy);
        Assert.Equal("/api/templates", Routes.Templates);

        Assert.Equal("/api/org-units/{id:guid}/settings", Routes.OrgUnitSettingsPattern);
        Assert.Equal("/api/org-units/{id:guid}/settings/{name}", Routes.OrgUnitSettingPattern);
        Assert.Equal("/api/org-units/{id:guid}/policies", Routes.OrgUnitPoliciesPattern);
        Assert.Equal("/api/org-units/{id:guid}/policies/{key}", Routes.OrgUnitPolicyPattern);
        Assert.Equal("/api/templates/{id:guid}", Routes.TemplatePattern);
        Assert.Equal("/api/templates/{id:guid}/apply", Routes.TemplateApplyPattern);

        Assert.Equal($"/api/org-units/{id}/settings", Routes.OrgUnitSettings(id));
        Assert.Equal($"/api/org-units/{id}/settings/Ui.DefaultCulture", Routes.OrgUnitSetting(id, "Ui.DefaultCulture"));
        Assert.Equal($"/api/org-units/{id}/policies", Routes.OrgUnitPolicies(id));
        Assert.Equal($"/api/org-units/{id}/policies/CreateOrgUnits", Routes.OrgUnitPolicy(id, PolicyCatalog.CreateOrgUnits));
        Assert.Equal($"/api/templates/{id}", Routes.Template(id));
        Assert.Equal($"/api/templates/{id}/apply", Routes.TemplateApply(id));
    }

    [Fact]
    public void Template_detail_round_trips_with_placeholders_and_arabic()
    {
        var summary = new OrgTemplateSummaryDto(Guid.NewGuid(), "مدرسة {Year}", "School {Year}", TemplateVisibilities.Shared,
            Guid.NewGuid(), "الجذر", "Root", 2, ["Year"], new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        var nodes = new[]
        {
            new TemplateNodeDto(1, null, Guid.NewGuid(), "مدرسة {Year}", "School {Year}", "S-{Year}", false, null, false,
                [PolicyCatalog.CreateOrgUnits], 1),
            new TemplateNodeDto(2, 1, Guid.NewGuid(), "الروضة", "KG", "KG", true, null, true, [], 0),
        };
        var detail = new OrgTemplateDetailDto(summary, nodes);

        var back = JsonSerializer.Deserialize<OrgTemplateDetailDto>(JsonSerializer.Serialize(detail, Web), Web)!;

        Assert.Equal(summary.NameAr, back.Summary.NameAr);
        Assert.Equal(summary.Placeholders, back.Summary.Placeholders);
        Assert.Equal(2, back.Nodes.Count);
        Assert.Equal(1, back.Nodes[1].ParentKey);
        Assert.Equal(nodes[0].PolicyKeys, back.Nodes[0].PolicyKeys);
    }

    [Fact]
    public void Requests_and_results_round_trip()
    {
        var save = new SaveTemplateRequest(Guid.NewGuid(), "قالب", "Template", TemplateVisibilities.Private, true, false,
            new Dictionary<string, string> { ["2026"] = "{Year}" });
        var apply = new ApplyTemplateRequest(Guid.NewGuid(), new Dictionary<string, string> { ["Year"] = "2027" });
        var copy = new CopyBranchRequest(Guid.NewGuid(), Guid.NewGuid(), "-2", true, true, null, "Copy");
        var setting = new SetSettingRequest("true", true);
        var effective = new EffectiveSettingDto("Ui.ShowHijriDates", "true", true, false, true, Guid.NewGuid(), "الجذر", "Root");
        var policy = new EffectivePolicyDto(PolicyCatalog.CreateOrgUnits, PolicyStates.Inherited, Guid.NewGuid(), "الجذر", "Root");

        Assert.Equal("{Year}", RoundTrip(save).Parameterize["2026"]);
        Assert.Equal("2027", RoundTrip(apply).Placeholders["Year"]);
        Assert.Equal(copy, RoundTrip(copy));
        Assert.Equal(setting, RoundTrip(setting));
        Assert.Equal(effective, RoundTrip(effective));
        Assert.Equal(policy, RoundTrip(policy));
        Assert.Equal(["x"], RoundTrip(new CopyBranchResultDto(Guid.NewGuid(), 3, ["x"])).SkippedLockedSettings);
        Assert.Equal(4, RoundTrip(new ApplyTemplateResultDto(Guid.NewGuid(), 4, [])).UnitsCreated);
        Assert.Equal(["OrgUnits.Create"],
            RoundTrip(new PolicyDefinitionDto(PolicyCatalog.CreateOrgUnits, ["OrgUnits.Create"])).BlockedPermissions);
        Assert.Null(RoundTrip(new PolicyDefinitionDto(PolicyCatalog.CreateOrgUnits, ["OrgUnits.Create"])).BlockedRoles);
        Assert.Equal(["Teacher"],
            RoundTrip(new PolicyDefinitionDto(PolicyCatalog.CreateTeachers, [], ["Teacher"])).BlockedRoles!);
    }

    [Fact]
    public void Setting_definition_round_trips_with_optional_fields()
    {
        var definition = SettingCatalog.Find("Files.MaxUploadMb")!;
        var back = RoundTrip(definition);

        Assert.Equal((definition.Name, definition.Min, definition.Max), (back.Name, back.Min, back.Max));
        Assert.Null(back.AllowedValues);
    }

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Web), Web)!;
}
