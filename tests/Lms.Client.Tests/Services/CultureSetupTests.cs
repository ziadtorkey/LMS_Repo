using System.Globalization;
using Bunit;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using MudBlazor;

namespace Lms.Client.Tests.Services;

/// <summary>
/// #508: under the Arabic culture .NET writes negative numbers with the Arabic minus sign, which starts with U+061C
/// (ARABIC LETTER MARK). Browsers reject attributes such as <c>tabindex="؜-1"</c>. The app keeps the Arabic UI culture
/// and Gregorian dates, but formats numbers the invariant way.
/// </summary>
public sealed class CultureSetupTests : ClientTestContext
{
    private const char ArabicLetterMark = '؜';

    [Theory]
    [InlineData("ar")]
    [InlineData("ar-SA")]
    [InlineData("en")]
    public void Lms_culture_formats_numbers_the_invariant_way(string name)
    {
        var culture = CultureSetup.ForLms(new CultureInfo(name));

        Assert.Equal("-1", (-1).ToString(culture));
        Assert.Equal("-1.5", (-1.5m).ToString(culture));
        Assert.Equal("1,234", 1234.ToString("N0", culture));
        Assert.DoesNotContain(ArabicLetterMark, (-42).ToString(culture));
    }

    [Fact]
    public void Lms_culture_keeps_the_arabic_ui_and_gregorian_dates()
    {
        var culture = CultureSetup.ForLms(new CultureInfo("ar-SA"));

        Assert.Equal("ar-SA", culture.Name);
        Assert.True(culture.TextInfo.IsRightToLeft);
        Assert.IsType<GregorianCalendar>(culture.DateTimeFormat.Calendar);
        Assert.Equal(CultureSetup.WithGregorianCalendar(new CultureInfo("ar-SA")).DateTimeFormat.MonthNames[0], culture.DateTimeFormat.MonthNames[0]);
    }

    [Fact]
    public void Lms_culture_does_not_change_the_culture_it_was_given()
    {
        var original = new CultureInfo("ar");
        var sign = original.NumberFormat.NegativeSign;

        _ = CultureSetup.ForLms(original);

        Assert.Equal(sign, original.NumberFormat.NegativeSign);
    }

    [Fact]
    public void Rendered_numeric_attributes_carry_no_arabic_letter_mark()
    {
        // The test context applies the same culture setup as the app (Arabic by default).
        Assert.Equal("ar", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

        var chip = Render<MudChip<string>>(p => p.Add(x => x.Text, "مجدولة"));
        var tabs = Render<MudTabs>(p => p.AddChildContent<MudTabPanel>(t => t.Add(x => x.Text, "الخطة")));

        Assert.DoesNotContain(ArabicLetterMark, chip.Markup);
        Assert.DoesNotContain(ArabicLetterMark, tabs.Markup);
        Assert.Contains("tabindex=\"-1\"", chip.Markup);
    }
}
