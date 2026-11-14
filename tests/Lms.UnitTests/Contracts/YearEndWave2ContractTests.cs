using System.Reflection;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.IdentityProviders;
using Lms.Shared.YearEnd;

namespace Lms.UnitTests.Contracts;

/// <summary>M7 wave 2: SAML and LDAP settings (D85), the undo-window and checklist settings, the withdrawal-reasons permission, the year-end checklist, and the secrets rule.</summary>
public sealed class YearEndWave2ContractTests
{
    private static bool Nullable(PropertyInfo property) => new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;

    [Fact]
    public void The_reasons_list_is_written_with_its_own_permission_held_by_the_org_admin_only()
    {
        Assert.Equal("Students.ManageWithdrawalReasons", Permissions.Students.ManageWithdrawalReasons);
        Assert.Contains(Permissions.Students.ManageWithdrawalReasons, Permissions.All);
        foreach (var role in new[] { BuiltInRoles.Manager, BuiltInRoles.Counselor, BuiltInRoles.Supervisor, BuiltInRoles.Teacher, BuiltInRoles.Student, BuiltInRoles.Guardian })
        {
            Assert.DoesNotContain(Permissions.Students.ManageWithdrawalReasons, BuiltInRoles.Get(role).DefaultPermissions);
        }

        Assert.Contains(Permissions.Students.ManageWithdrawalReasons, BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions);
        Assert.Equal(12, Lms.Infrastructure.Access.BuiltInRoleDefaults.IntroducedIn(Permissions.Students.ManageWithdrawalReasons));
    }

    [Fact]
    public void The_two_year_settings_are_client_level_with_their_defaults_and_bounds()
    {
        var undo = SettingCatalog.Find("Years.PromotionUndoWindowMinutes")!;
        var checklist = SettingCatalog.Find("Years.EndChecklistDays")!;

        Assert.Equal(("10", 1, 60, true), (undo.DefaultJson, undo.Min, undo.Max, undo.ClientLevelOnly));
        Assert.Equal(("60", 7, 180, true), (checklist.DefaultJson, checklist.Min, checklist.Max, checklist.ClientLevelOnly));
        Assert.Equal(PromotionLimits.DefaultUndoWindowMinutes, SettingKeys.YearsPromotionUndoWindowMinutes.Default);
        Assert.Equal(60, SettingKeys.YearsEndChecklistDays.Default);
    }

    [Fact]
    public void The_provider_kinds_and_option_lists_are_pinned()
    {
        Assert.Equal(["Oidc", "AzureAd", "Saml", "Ldap"], IdentityProviderKinds.All);
        Assert.Equal(["Ldaps", "StartTls"], LdapTlsModes.All); // never plain ldap://
        Assert.Equal(["EmailAddress", "Persistent", "Unspecified"], SamlNameIdFormats.All);
        Assert.Equal("{0}", SamlLdapLimits.FilterUserPlaceholder);
        Assert.Equal(3, SamlLdapLimits.MaxSigningCertificates);
    }

    [Fact]
    public void No_read_dto_ever_returns_a_secret()
    {
        var reads = new[] { typeof(IdentityProviderDto), typeof(SamlSettingsDto), typeof(LdapSettingsDto), typeof(CertificateInfoDto), typeof(SamlSpMetadataDto), typeof(ProviderTestResultDto), typeof(ProviderTestCheckDto) };
        var banned = new[] { "Secret", "Password", "Pem", "PrivateKey" };

        foreach (var property in reads.SelectMany(t => t.GetProperties()))
        {
            var isFlag = property.Name.StartsWith("Has", StringComparison.Ordinal) && property.PropertyType == typeof(bool);
            Assert.True(isFlag || !banned.Any(b => property.Name.Contains(b, StringComparison.OrdinalIgnoreCase)), property.DeclaringType!.Name + "." + property.Name);
        }

        Assert.NotNull(typeof(LdapSettingsDto).GetProperty(nameof(LdapSettingsDto.HasBindPassword)));
        Assert.NotNull(typeof(LdapSettingsDto).GetProperty(nameof(LdapSettingsDto.HasCustomCa)));
        Assert.Equal(["Thumbprint", "Subject", "NotAfterUtc"], typeof(CertificateInfoDto).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public void The_write_side_keeps_a_stored_secret_when_the_field_is_null()
    {
        Assert.True(Nullable(typeof(LdapSettingsRequest).GetProperty(nameof(LdapSettingsRequest.BindPassword))!));
        Assert.True(Nullable(typeof(LdapSettingsRequest).GetProperty(nameof(LdapSettingsRequest.CustomCaCertificatePem))!));
        Assert.True(Nullable(typeof(SamlSettingsRequest).GetProperty(nameof(SamlSettingsRequest.SigningCertificatesPem))!));
    }

    [Fact]
    public void The_provider_dtos_gain_the_new_settings_as_optional_trailing_fields_so_old_callers_compile()
    {
        var dto = new IdentityProviderDto(Guid.NewGuid(), "Oidc", "a", "ا", "A", "https://a", null, "c", true, "openid", "Email", null, [], true, "https://cb");
        var request = new SaveIdentityProviderRequest("Oidc", "a", "ا", "A", "https://a", null, "c", null, "openid", "Email", null, [], true);
        var test = new ProviderTestResultDto(true, "https://a", null);

        Assert.Null(dto.Saml);
        Assert.Null(dto.Ldap);
        Assert.Null(request.Saml);
        Assert.Null(request.Ldap);
        Assert.Null(test.Checks);
    }

    [Fact]
    public void Test_check_codes_are_unique_and_the_failure_codes_exist()
    {
        Assert.Equal(ProviderTestCheckCodes.All.Count, ProviderTestCheckCodes.All.Distinct().Count());
        Assert.Contains("ServiceBindSucceeded", ProviderTestCheckCodes.All);
        Assert.Equal("provider_test_failed", ProblemCodes.provider_test_failed);
        Assert.Equal("active_year_exists", ProblemCodes.active_year_exists); // exists since M3
    }

    [Fact]
    public void Sign_in_pages_and_the_metadata_route_are_pinned()
    {
        Assert.Equal("/Account/Saml/{slug}", ExternalSignInPages.SamlLogin);
        Assert.Equal("/Account/Saml/{slug}/acs", ExternalSignInPages.SamlAcs);
        Assert.Equal("/Account/Saml/{slug}/metadata", ExternalSignInPages.SamlMetadata);
        Assert.Equal("/Account/Ldap/{slug}", ExternalSignInPages.LdapLogin);
        Assert.Equal("/Account/Saml/uni/acs", ExternalSignInPages.SamlAcsFor("uni"));
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Assert.Equal("/api/identity-providers/11111111-1111-1111-1111-111111111111/saml/metadata", Routes.IdentityProviderSamlMetadata(id));
        Assert.Equal("/api/identity-providers/{id:guid}/saml/metadata", Routes.IdentityProviderSamlMetadataPattern);
    }

    [Fact]
    public void The_year_end_checklist_route_steps_and_states_are_pinned_and_it_carries_no_student_data()
    {
        Assert.Equal("/api/me/year-end-checklist", Routes.MeYearEndChecklist);
        Assert.Equal(["PublishGrades", "ApproveGradeChanges", "CloseYear", "RunPromotion", "OpenNextYear"], YearEndChecklistSteps.All);
        Assert.Equal(["Todo", "Warning", "Done", "NotApplicable"], YearEndChecklistStates.All);
        var names = new[] { typeof(YearEndChecklistDto), typeof(YearEndChecklistYearDto), typeof(YearEndChecklistStepDto) }.SelectMany(t => t.GetProperties()).Select(p => p.Name);
        Assert.DoesNotContain(names, n => n.Contains("Student", StringComparison.Ordinal));
    }

    [Fact]
    public void The_close_answers_say_when_lock_snapshots_are_still_being_finalised()
    {
        var closed = new YearClosedDto(Guid.NewGuid(), DateTime.UtcNow, 1, 2, DateTime.UtcNow);
        var report = new ClosingReportDto(Guid.NewGuid(), DateTime.UtcNow, []);

        Assert.False(closed.LocksFinalising);
        Assert.False(report.LocksFinalising);
        Assert.True((closed with { LocksFinalising = true }).LocksFinalising);
    }

    [Fact]
    public void The_reasons_list_has_limits_and_the_audit_catalog_names_the_new_entities()
    {
        Assert.Equal((30, 50, 100), (WithdrawalReasonLimits.MaxReasons, WithdrawalReasonLimits.KeyMaxLength, WithdrawalReasonLimits.NameMaxLength));
        Assert.Contains("WithdrawalReason", Lms.Shared.Auditing.AuditCatalog.EntityTypes);
        Assert.Contains("YearOpenRun", Lms.Shared.Auditing.AuditCatalog.EntityTypes);
    }

    [Fact]
    public void A_manager_cannot_create_edit_or_assign_a_role_that_holds_AcademicYears_Close_or_the_reasons_permission()
    {
        var manager = BuiltInRoles.Get(BuiltInRoles.Manager).DefaultPermissions.ToHashSet();

        foreach (var permission in new[] { Permissions.AcademicYears.Close, Permissions.Students.ManageWithdrawalReasons })
        {
            Assert.DoesNotContain(permission, manager);
            // creating or editing a role that holds it (D16, the same rule the role editor and the assignment endpoint use)
            Assert.Equal(ProblemCodes.permission_escalation, Lms.Infrastructure.Access.RolePermissionRules.Validate([permission], [], manager, false, null));
            // assigning an existing role that holds it: the role's current permissions are beyond the caller
            Assert.False(Lms.Infrastructure.Access.RolePermissionRules.CanModify([permission], manager, false));
        }

        // the Org Admin holds both, so the Org Admin may
        var admin = BuiltInRoles.Get(BuiltInRoles.OrgAdmin).DefaultPermissions.ToHashSet();
        Assert.Null(Lms.Infrastructure.Access.RolePermissionRules.Validate([Permissions.AcademicYears.Close], [], admin, false, null));
    }


    [Fact]
    public void A_promotion_run_carries_the_display_names_of_its_years_and_unit_for_the_history()
    {
        var names = typeof(PromotionRunDto).GetProperties().Select(p => p.Name).ToList();

        foreach (var expected in new[] { "SourceYearNameAr", "SourceYearNameEn", "TargetYearNameAr", "TargetYearNameEn", "SourceUnitNameAr", "SourceUnitNameEn" })
        {
            Assert.Contains(expected, names);
        }

        var run = new PromotionRunDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, PromotionRunStates.Draft, 0, 0, 0,
            new PromotionCountsDto(0, 0, 0, 0, 0), [], [], null, DateTime.UtcNow, null, Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal(string.Empty, run.SourceYearNameEn); // optional trailing fields: older callers compile
    }
}
