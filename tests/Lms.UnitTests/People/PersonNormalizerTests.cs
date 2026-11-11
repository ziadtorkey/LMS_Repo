using Lms.Domain.People;

namespace Lms.UnitTests.People;

public sealed class PersonNormalizerTests
{
    [Fact]
    public void NameKey_unifies_alef_forms_ta_marbuta_ya_and_strips_diacritics()
    {
        Assert.Equal(PersonNormalizer.NameKey("أحمد"), PersonNormalizer.NameKey("إحمد"));
        Assert.Equal(PersonNormalizer.NameKey("أحمد"), PersonNormalizer.NameKey("احمد"));
        Assert.Equal(PersonNormalizer.NameKey("فاطمة"), PersonNormalizer.NameKey("فاطمه"));
        Assert.Equal(PersonNormalizer.NameKey("مصطفى"), PersonNormalizer.NameKey("مصطفي"));
        Assert.Equal(PersonNormalizer.NameKey("مُحَمَّد"), PersonNormalizer.NameKey("محمد"));
        Assert.Equal(PersonNormalizer.NameKey("محمـــد"), PersonNormalizer.NameKey("محمد"));
    }

    [Fact]
    public void NameKey_trims_collapses_whitespace_and_lower_cases()
    {
        Assert.Equal("ahmed ali", PersonNormalizer.NameKey("  Ahmed   ALI \t"));
        Assert.Equal(string.Empty, PersonNormalizer.NameKey("   "));
        Assert.Equal(string.Empty, PersonNormalizer.NameKey(null));
    }

    [Theory]
    [InlineData("+20 100-123 4567", "+201001234567")]
    [InlineData("+201001234567", "+201001234567")]
    [InlineData("(010) 0123-4567", "01001234567")]
    [InlineData("12345", null)]
    [InlineData("+2010012345678901234", null)]
    [InlineData("0100abc4567", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Mobile_strips_formatting_and_rejects_short_numbers(string? input, string? expected)
    {
        Assert.Equal(expected, PersonNormalizer.Mobile(input));
        Assert.Equal(expected is not null, PersonNormalizer.IsValidMobile(input));
    }

    [Theory]
    [InlineData(" ab12345 ", "AB12345")]
    [InlineData("29001011234567", "29001011234567")]
    [InlineData("AB-12345", null)]
    [InlineData("AB 12345", null)]
    [InlineData("1234", null)]
    [InlineData("123456789012345678901", null)]
    [InlineData(null, null)]
    public void NationalId_upper_cases_and_rejects_symbols(string? input, string? expected)
    {
        Assert.Equal(expected, PersonNormalizer.NationalId(input));
        Assert.Equal(expected is not null, PersonNormalizer.IsValidNationalId(input));
    }

    [Fact]
    public void Email_is_trimmed_and_lower_cased()
    {
        Assert.Equal("a.b@lms.test", PersonNormalizer.Email("  A.B@LMS.test "));
        Assert.Null(PersonNormalizer.Email("  "));
        Assert.Null(PersonNormalizer.Email(null));
    }
}
