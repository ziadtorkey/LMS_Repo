using System.Globalization;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.IdentityProviders;
using Lms.Shared.YearEnd;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

/// <summary>M7 wave 2: every new label exists in Arabic and English.</summary>
public sealed class YearEndWave2ResourceTests : ClientTestContext
{
    private static IEnumerable<string> Keys()
    {
        foreach (var kind in new[] { IdentityProviderKinds.Saml, IdentityProviderKinds.Ldap })
        {
            yield return "IdpKind_" + kind;
        }

        foreach (var mode in LdapTlsModes.All)
        {
            yield return "LdapTls_" + mode;
        }

        foreach (var format in SamlNameIdFormats.All)
        {
            yield return "SamlNameId_" + format;
        }

        foreach (var code in ProviderTestCheckCodes.All)
        {
            yield return "ProviderCheck_" + code;
        }

        foreach (var step in YearEndChecklistSteps.All)
        {
            yield return "YearStep_" + step;
        }

        foreach (var state in YearEndChecklistStates.All)
        {
            yield return "YearStepState_" + state;
        }

        yield return "Perm_Students_ManageWithdrawalReasons";
        yield return "Setting_Years_PromotionUndoWindowMinutes";
        yield return "Setting_Years_EndChecklistDays";
        yield return "AuditEntity_WithdrawalReason";
        yield return "AuditEntity_YearOpenRun";
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_wave_2_label_exists(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        Assert.Empty(Keys().Where(k => l[k].ResourceNotFound).ToList());
    }
}
