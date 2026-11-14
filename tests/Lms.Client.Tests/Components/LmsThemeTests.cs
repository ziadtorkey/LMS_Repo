using Lms.Client.Theme;
using MudBlazor;
using MudBlazor.Utilities;

namespace Lms.Client.Tests.Components;

/// <summary>The MudBlazor theme follows the Stitch design system (docs/design/stitch, #553) in both languages and both modes.</summary>
public sealed class LmsThemeTests
{
    private static string Hex(MudColor c) => c.ToString(MudColorOutputFormats.Hex).ToUpperInvariant();

    [Fact]
    public void Arabic_ui_uses_Cairo_first()
    {
        var theme = LmsTheme.Create(rightToLeft: true);

        Assert.Equal("Cairo", theme.Typography.Default.FontFamily![0]);
        Assert.Equal("Cairo", theme.Typography.H4.FontFamily![0]);
    }

    [Fact]
    public void English_ui_uses_Inter_with_Cairo_for_arabic_names()
    {
        var stack = LmsTheme.Create(rightToLeft: false).Typography.Default.FontFamily!;

        Assert.Equal("Inter", stack[0]);
        Assert.DoesNotContain(stack, f => f.Contains("Aptos", StringComparison.Ordinal));
        Assert.Contains("Cairo", stack);
        Assert.Equal("sans-serif", stack[^1]);
    }

    [Fact]
    public void Headings_use_Cairo_in_both_languages_and_body_text_follows_the_language()
    {
        var en = LmsTheme.Create(rightToLeft: false).Typography;

        Assert.All(new BaseTypography[] { en.H4, en.H5, en.H6 }, t => Assert.Equal("Cairo", t.FontFamily![0]));
        Assert.Equal("Inter", en.Body1.FontFamily![0]);
        Assert.Equal("Inter", en.Body2.FontFamily![0]);
    }

    [Fact]
    public void Page_title_is_32px_bold_and_section_titles_18px_semibold()
    {
        var type = LmsTheme.Create(rightToLeft: true).Typography;

        Assert.Equal(("2rem", "700"), (type.H4.FontSize, type.H4.FontWeight));
        Assert.Equal(("1.125rem", "600"), (type.H6.FontSize, type.H6.FontWeight));
        Assert.Equal("none", type.Button.TextTransform);
        Assert.Equal("none", type.Overline.TextTransform);
    }

    [Fact]
    public void Brand_green_is_primary_in_light_mode_and_a_lighter_green_in_dark_mode()
    {
        var theme = LmsTheme.Create(rightToLeft: false);

        Assert.Equal("#00663D", Hex(theme.PaletteLight.Primary));
        Assert.Equal("#4CC38A", Hex(theme.PaletteDark.Primary));
    }

    [Fact]
    public void Canvas_surfaces_and_borders_follow_stitch()
    {
        var theme = LmsTheme.Create(rightToLeft: false);

        Assert.Equal(("#F8FAFC", "#FFFFFF", "#E2E8F0"), (Hex(theme.PaletteLight.Background), Hex(theme.PaletteLight.Surface), Hex(theme.PaletteLight.LinesDefault)));
        Assert.Equal(("#090D10", "#121A20", "#1E293B"), (Hex(theme.PaletteDark.Background), Hex(theme.PaletteDark.Surface), Hex(theme.PaletteDark.LinesDefault)));
        Assert.Equal("260px", theme.LayoutProperties.DrawerWidthLeft);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Text_and_semantic_colours_reach_wcag_aa_on_their_surface(bool dark)
    {
        var theme = LmsTheme.Create(rightToLeft: false);
        Palette p = dark ? theme.PaletteDark : theme.PaletteLight;
        foreach (var bg in new[] { p.Surface, p.Background })
        {
            foreach (var (name, fg) in new[] { ("TextPrimary", p.TextPrimary), ("TextSecondary", p.TextSecondary), ("Primary", p.Primary),
                         ("Success", p.Success), ("Warning", p.Warning), ("Error", p.Error), ("Info", p.Info) })
            {
                var ratio = Contrast(fg, bg);
                Assert.True(ratio >= 4.5, $"{name} {Hex(fg)} on {Hex(bg)} is {ratio:0.00}:1 ({(dark ? "dark" : "light")})");
            }
            Assert.True(Contrast(p.LinesInputs, bg) >= 3, $"LinesInputs on {Hex(bg)} below 3:1");
        }
    }

    private static double Contrast(MudColor a, MudColor b)
    {
        static double L(MudColor c)
        {
            static double Ch(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var (la, lb) = (L(a), L(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
