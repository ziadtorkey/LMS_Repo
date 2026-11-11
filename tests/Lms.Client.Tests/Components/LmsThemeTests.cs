using Lms.Client.Theme;

namespace Lms.Client.Tests.Components;

public sealed class LmsThemeTests
{
    [Fact]
    public void Arabic_ui_uses_Cairo_first()
    {
        var theme = LmsTheme.Create(rightToLeft: true);

        Assert.Equal("Cairo", theme.Typography.Default.FontFamily![0]);
        Assert.Equal("Cairo", theme.Typography.H4.FontFamily![0]);
    }

    [Fact]
    public void English_ui_prefers_Aptos_with_a_hosted_free_fallback_and_Cairo_for_arabic_names()
    {
        var stack = LmsTheme.Create(rightToLeft: false).Typography.Default.FontFamily!;

        Assert.Equal(["Aptos Display", "Aptos", "Inter"], stack.Take(3));
        Assert.Contains("Cairo", stack);
        Assert.Equal("sans-serif", stack[^1]);
    }

    [Fact]
    public void Brand_green_is_primary_in_light_mode_and_a_lighter_green_in_dark_mode()
    {
        var theme = LmsTheme.Create(rightToLeft: false);

        Assert.Equal("#00663D", theme.PaletteLight.Primary.ToString(MudBlazor.Utilities.MudColorOutputFormats.Hex), ignoreCase: true);
        Assert.Equal("#4CC38A", theme.PaletteDark.Primary.ToString(MudBlazor.Utilities.MudColorOutputFormats.Hex), ignoreCase: true);
    }
}
