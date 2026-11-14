using System.Globalization;
using Lms.Client.Resources;
using Lms.Client.Tests.Support;
using Lms.Shared;
using Lms.Shared.Configuration;
using Lms.Shared.Grading;
using Lms.Shared.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

/// <summary>M5 wave 1: every grading code, event, setting and catalog value has a label in both languages (L26: no grade value in a notification text).</summary>
public sealed class GradingResourceTests : ClientTestContext
{
    private static readonly string[] Codes =
    [
        ProblemCodes.gradebook_locked, ProblemCodes.scale_provisional, ProblemCodes.scale_bands_invalid, ProblemCodes.scale_in_use,
        ProblemCodes.weights_invalid, ProblemCodes.grade_out_of_range, ProblemCodes.grade_override_reason_required,
        ProblemCodes.grade_import_invalid, ProblemCodes.grade_import_expired, ProblemCodes.terms_have_dependants,
        ProblemCodes.concurrency_conflict,
    ];

    private static IEnumerable<string> Keys()
    {
        foreach (var code in Codes)
        {
            yield return "Error_" + code;
        }

        foreach (var name in new[]
                 {
                     SettingKeys.OrgTimeZone.Name, SettingKeys.GradesMissingPolicy.Name, SettingKeys.GradesCategoryMethod.Name,
                     SettingKeys.GradesDecimalPlaces.Name, SettingKeys.AcademicYearsTermsPerYear.Name,
                 })
        {
            yield return "Setting_" + name.Replace('.', '_');
        }

        foreach (var value in MissingPolicies.All)
        {
            yield return "SettingValue_Grades_MissingPolicy_" + value;
        }

        foreach (var value in CategoryMethods.All)
        {
            yield return "SettingValue_Grades_CategoryMethod_" + value;
        }

        yield return "NotifEvent_" + NotificationEvents.GradesPublished.Replace('.', '_');
        yield return "Notif_" + NotificationEvents.GradesPublished.Replace('.', '_');

        foreach (var v in ScaleKinds.All)
        {
            yield return "Grd_ScaleKind_" + v;
        }

        foreach (var v in PublishStates.All)
        {
            yield return "Grd_PublishState_" + v;
        }

        foreach (var v in GradeCellStates.All)
        {
            yield return "Grd_CellState_" + v;
        }

        foreach (var v in GradebookStates.All)
        {
            yield return "Grd_GradebookState_" + v;
        }

        foreach (var v in PublishModes.All)
        {
            yield return "Grd_PublishMode_" + v;
        }
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Every_grading_label_exists(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var missing = Keys().Where(k => l[k].ResourceNotFound).ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_grades_published_text_names_only_the_course(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var l = Services.GetRequiredService<IStringLocalizer<SharedResource>>();

        var text = l["Notif_Grades_Published"].Value;

        Assert.Contains("{CourseName}", text);
        Assert.DoesNotContain("{Points}", text);
        Assert.DoesNotContain("{Score}", text);
        Assert.DoesNotContain("{Title}", text);
        Assert.Equal(1, text.Count(c => c == '{')); // the course name is the only placeholder
    }
}
