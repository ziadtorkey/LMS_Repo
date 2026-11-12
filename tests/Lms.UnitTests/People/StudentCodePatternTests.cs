using Lms.Domain.People;
using Lms.Infrastructure.Configuration;
using Lms.Shared.Configuration;

namespace Lms.UnitTests.People;

public sealed class StudentCodePatternTests
{
    private static StudentCodePattern Parse(string pattern)
    {
        Assert.True(StudentCodePattern.TryParse(pattern, out var parsed, out var error), error);
        return parsed!;
    }

    private static readonly StudentCodeTokens Tokens = new("ABC");

    [Fact]
    public void Default_pattern_renders_six_digit_seq()
    {
        var pattern = Parse("{Seq:6}");

        Assert.Equal("000007", pattern.Render(Tokens, 7));
        Assert.Equal(6, pattern.SeqDigits);
        Assert.Equal(string.Empty, pattern.Prefix(Tokens));
    }

    [Fact]
    public void RootCode_and_literal_prefix_render()
    {
        var pattern = Parse("G-{RootCode}-{Seq:5}");

        Assert.Equal("G-ABC-00042", pattern.Render(Tokens, 42));
        Assert.Equal("G-ABC-", pattern.Prefix(Tokens));
        Assert.Equal(5, pattern.SeqDigits);
    }

    [Fact]
    public void Literal_after_the_seq_is_allowed()
    {
        Assert.Equal("00012-S", Parse("{Seq:5}-S").Render(Tokens, 12));
    }

    [Fact]
    public void Seq_without_a_value_renders_zeros_for_previews()
    {
        Assert.Equal("000", Parse("{Seq:3}").Render(Tokens, null));
    }

    [Theory]
    [InlineData("S-")]
    [InlineData("{RootCode}")]
    [InlineData("")]
    public void Seq_token_is_required(string pattern)
    {
        Assert.False(StudentCodePattern.TryParse(pattern, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.Equal("seq_token_required", error);
    }

    [Theory]
    [InlineData("{Seq:3}{Seq:3}")]
    [InlineData("{Seq:3}-{Seq:4}")]
    public void Seq_token_must_appear_exactly_once(string pattern)
    {
        Assert.False(StudentCodePattern.TryParse(pattern, out _, out var error));
        Assert.Equal("seq_token_repeated", error);
    }

    [Fact]
    public void RootCode_may_appear_at_most_once()
    {
        Assert.False(StudentCodePattern.TryParse("{RootCode}{RootCode}{Seq:3}", out _, out var error));
        Assert.Equal("root_code_repeated", error);
    }

    [Theory]
    [InlineData("{SchoolCode}-{Seq:4}")]
    [InlineData("{Year}{Seq:4}")]
    [InlineData("{YY}-{Seq:4}")]
    [InlineData("{UnitCode}{Seq:4}")]
    [InlineData("{Seq}")]
    [InlineData("{seq:4}")]
    [InlineData("{Seq:4")]
    public void Unknown_token_SchoolCode_Year_or_symbol_is_invalid(string pattern)
    {
        Assert.False(StudentCodePattern.TryParse(pattern, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("S/{Seq:4}")]
    [InlineData("S {Seq:4}")]
    [InlineData("S_{Seq:4}")]
    [InlineData("طالب-{Seq:4}")]
    public void Literal_text_allows_only_letters_digits_and_hyphen(string pattern)
    {
        Assert.False(StudentCodePattern.TryParse(pattern, out _, out var error));
        Assert.Equal("invalid_literal", error);
    }

    [Theory]
    [InlineData("{Seq:0}")]
    [InlineData("{Seq:10}")]
    [InlineData("{Seq:-1}")]
    [InlineData("{Seq:x}")]
    public void Seq_digits_must_be_1_to_9(string pattern)
    {
        Assert.False(StudentCodePattern.TryParse(pattern, out _, out var error));
        Assert.Equal("seq_digits_out_of_range", error);
    }

    [Fact]
    public void Pattern_whose_fixed_part_cannot_fit_30_characters_is_invalid()
    {
        var literal = new string('A', 25);

        Assert.False(StudentCodePattern.TryParse(literal + "{Seq:6}", out _, out var error));
        Assert.Equal("too_long", error);
        Assert.True(StudentCodePattern.TryParse(new string('A', 24) + "{Seq:6}", out _, out _)); // exactly 30
    }

    [Fact]
    public void Max_value_is_one_below_ten_to_the_digits()
    {
        Assert.Equal(999, Parse("{Seq:3}").MaxValue);
        Assert.Equal(999_999_999, Parse("{Seq:9}").MaxValue);
    }

    [Theory]
    [InlineData("{Seq:6}", true)]
    [InlineData("{Seq:99}", false)]
    [InlineData("", false)]
    public void Code_pattern_setting_must_parse(string pattern, bool valid)
    {
        var definition = SettingCatalog.Find(SettingKeys.StudentsCodePattern.Name)!;

        var error = SettingValueValidator.Validate(definition, System.Text.Json.JsonSerializer.Serialize(pattern));

        Assert.Equal(valid ? null : "invalid_code_pattern", error);
    }
}
