using Lms.Infrastructure.Configuration;
using Lms.Shared.Configuration;

namespace Lms.UnitTests.Configuration;

public sealed class SettingValueValidatorTests
{
    private static SettingDefinition Def(string name) => SettingCatalog.Find(name)!;

    private static readonly SettingDefinition IntSetting = Def(SettingKeys.FilesMaxUploadMb.Name);
    private static readonly SettingDefinition EnumSetting = Def(SettingKeys.UiDefaultCulture.Name);
    private static readonly SettingDefinition BoolSetting = Def(FeatureFlags.VirtualClasses.Name);

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("2049")]
    [InlineData("100000")]
    public void Int_outside_range_is_invalid(string json) =>
        Assert.Equal("out_of_range", SettingValueValidator.Validate(IntSetting, json));

    [Theory]
    [InlineData("1")]
    [InlineData("200")]
    [InlineData("2048")]
    public void Int_inside_range_is_valid(string json) =>
        Assert.Null(SettingValueValidator.Validate(IntSetting, json));

    [Theory]
    [InlineData("\"200\"")]
    [InlineData("1.5")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[1]")]
    public void Int_must_be_an_integer(string json) =>
        Assert.Equal("must_be_integer", SettingValueValidator.Validate(IntSetting, json));

    [Theory]
    [InlineData("\"fr\"")]
    [InlineData("\"AR\"")]
    [InlineData("\"\"")]
    public void Enum_value_not_allowed_is_invalid(string json) =>
        Assert.Equal("not_allowed", SettingValueValidator.Validate(EnumSetting, json));

    [Theory]
    [InlineData("\"ar\"")]
    [InlineData("\"en\"")]
    public void Enum_value_allowed_is_valid(string json) =>
        Assert.Null(SettingValueValidator.Validate(EnumSetting, json));

    [Theory]
    [InlineData("1")]
    [InlineData("null")]
    public void Enum_must_be_a_string(string json) =>
        Assert.Equal("must_be_string", SettingValueValidator.Validate(EnumSetting, json));

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Bool_accepts_true_and_false(string json) =>
        Assert.Null(SettingValueValidator.Validate(BoolSetting, json));

    [Theory]
    [InlineData("\"true\"")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("\"yes\"")]
    public void Bool_accepts_only_true_false(string json) =>
        Assert.Equal("must_be_boolean", SettingValueValidator.Validate(BoolSetting, json));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("tru")]
    public void Malformed_json_is_invalid(string json) =>
        Assert.Equal("invalid_json", SettingValueValidator.Validate(BoolSetting, json));

    [Fact]
    public void Every_catalog_default_passes_its_own_validation()
    {
        foreach (var definition in SettingCatalog.All)
        {
            Assert.Null(SettingValueValidator.Validate(definition, definition.DefaultJson));
        }
    }

    [Fact]
    public void String_kind_accepts_strings_only_and_limits_length()
    {
        var text = new SettingDefinition("X.Text", SettingValueKinds.String, SettingCategories.General, "\"\"");

        Assert.Null(SettingValueValidator.Validate(text, "\"مرحبا 🌟\""));
        Assert.Equal("must_be_string", SettingValueValidator.Validate(text, "5"));
        Assert.Equal("too_long", SettingValueValidator.Validate(text, "\"" + new string('x', 501) + "\""));
    }
}
