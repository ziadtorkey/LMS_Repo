using MudBlazor;

namespace Lms.Client.Theme;

/// <summary>
/// The LMS look: brand green #00663D on calm neutral surfaces with a light sidebar; Cairo for Arabic and
/// Aptos Display for English. Colours are chosen for WCAG AA text contrast in both palettes.
/// Tokens the theme cannot express live in wwwroot/css/lms-theme.css (keep the font stacks there in sync).
/// </summary>
public static class LmsTheme
{
    /// <summary>
    /// Arabic UI: Cairo (Google Fonts) for Arabic and its own Latin glyphs.
    /// English UI: Aptos Display / Aptos when installed (Windows with Microsoft 365); Aptos is not licensed for
    /// web hosting, so devices without it get Inter (Google Fonts). Cairo stays in the stack for Arabic names.
    /// </summary>
    public static readonly string[] ArabicFontStack =
        ["Cairo", "Segoe UI", "Tahoma", "Arial", "sans-serif"];

    public static readonly string[] EnglishFontStack =
        ["Aptos Display", "Aptos", "Inter", "Segoe UI", "Cairo", "Arial", "sans-serif"];

    public static MudTheme Create(bool rightToLeft) => new()
    {
        PaletteLight = Light(),
        PaletteDark = Dark(),
        Typography = Type(rightToLeft ? ArabicFontStack : EnglishFontStack),
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "264px",
            DrawerWidthRight = "264px",
            AppbarHeight = "64px",
        },
        Shadows = SoftShadows(),
    };

    private static PaletteLight Light() => new()
    {
        Primary = "#00663D", // brand green: 7.1:1 on white
        PrimaryDarken = "#004D2E",
        PrimaryLighten = "#2E8A5F",
        PrimaryContrastText = "#FFFFFF",
        Secondary = "#3E4C5E",
        SecondaryContrastText = "#FFFFFF",
        Tertiary = "#8A5A00",
        TertiaryContrastText = "#FFFFFF",
        Info = "#1F5FA6",
        InfoContrastText = "#FFFFFF",
        Success = "#1D7344",
        SuccessContrastText = "#FFFFFF",
        Warning = "#9A5B00",
        WarningContrastText = "#FFFFFF",
        Error = "#B42318",
        ErrorContrastText = "#FFFFFF",
        Dark = "#1B2733",
        Background = "#F4F6F7",
        BackgroundGray = "#ECEFF1",
        Surface = "#FFFFFF",
        AppbarBackground = "#FFFFFF",
        AppbarText = "#17212B",
        DrawerBackground = "#FFFFFF",
        DrawerText = "#2A3540",
        DrawerIcon = "#5B6876",
        TextPrimary = "#17212B",
        TextSecondary = "#526070",
        TextDisabled = "rgba(23,33,43,0.42)",
        ActionDefault = "#526070",
        ActionDisabled = "rgba(23,33,43,0.32)",
        ActionDisabledBackground = "rgba(23,33,43,0.08)",
        LinesDefault = "#E1E6EA",
        LinesInputs = "#B9C3CC",
        TableLines = "#E7EBEE",
        TableHover = "rgba(0,102,61,0.04)",
        TableStriped = "rgba(23,33,43,0.02)",
        Divider = "#E1E6EA",
        DividerLight = "#EEF1F3",
        HoverOpacity = 0.06,
        Skeleton = "rgba(23,33,43,0.08)",
        OverlayDark = "rgba(13,24,30,0.5)",
    };

    private static PaletteDark Dark() => new()
    {
        Primary = "#4CC38A", // the brand green lightened for dark surfaces: 7.4:1 on #162024
        PrimaryDarken = "#36A874",
        PrimaryLighten = "#7AD6A9",
        PrimaryContrastText = "#032013",
        Secondary = "#AAB7C5",
        SecondaryContrastText = "#111A22",
        Tertiary = "#E3B04B",
        TertiaryContrastText = "#1D1400",
        Info = "#77AEEA",
        InfoContrastText = "#0B1B2C",
        Success = "#62C08B",
        SuccessContrastText = "#06200F",
        Warning = "#E5A647",
        WarningContrastText = "#231500",
        Error = "#F2786D",
        ErrorContrastText = "#2A0603",
        Dark = "#0B1114",
        Black = "#0B1114",
        Background = "#0F1518",
        BackgroundGray = "#151D21",
        Surface = "#162024",
        AppbarBackground = "#162024",
        AppbarText = "#E5ECEE",
        DrawerBackground = "#162024",
        DrawerText = "#D5DEE1",
        DrawerIcon = "#9DACB5",
        TextPrimary = "#E5ECEE",
        TextSecondary = "#9DACB5",
        TextDisabled = "rgba(229,236,238,0.4)",
        ActionDefault = "#9DACB5",
        ActionDisabled = "rgba(229,236,238,0.3)",
        ActionDisabledBackground = "rgba(229,236,238,0.1)",
        LinesDefault = "#28343A",
        LinesInputs = "#4A5A62",
        TableLines = "#243035",
        TableHover = "rgba(76,195,138,0.06)",
        TableStriped = "rgba(229,236,238,0.02)",
        Divider = "#28343A",
        DividerLight = "#1E292E",
        HoverOpacity = 0.08,
        Skeleton = "rgba(229,236,238,0.08)",
        OverlayDark = "rgba(0,0,0,0.6)",
    };

    private static Typography Type(string[] font) => new()
    {
        Default = new DefaultTypography { FontFamily = font, FontSize = ".9375rem", FontWeight = "400", LineHeight = "1.6", LetterSpacing = "normal" },
        H1 = new H1Typography { FontFamily = font, FontSize = "2.5rem", FontWeight = "600", LineHeight = "1.25", LetterSpacing = "-.01em" },
        H2 = new H2Typography { FontFamily = font, FontSize = "2rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.01em" },
        H3 = new H3Typography { FontFamily = font, FontSize = "1.75rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "normal" },
        H4 = new H4Typography { FontFamily = font, FontSize = "1.625rem", FontWeight = "600", LineHeight = "1.35", LetterSpacing = "normal" },
        H5 = new H5Typography { FontFamily = font, FontSize = "1.25rem", FontWeight = "600", LineHeight = "1.4", LetterSpacing = "normal" },
        H6 = new H6Typography { FontFamily = font, FontSize = "1.0625rem", FontWeight = "600", LineHeight = "1.5", LetterSpacing = "normal" },
        Subtitle1 = new Subtitle1Typography { FontFamily = font, FontSize = "1rem", FontWeight = "500", LineHeight = "1.6", LetterSpacing = "normal" },
        Subtitle2 = new Subtitle2Typography { FontFamily = font, FontSize = ".875rem", FontWeight = "600", LineHeight = "1.55", LetterSpacing = "normal" },
        Body1 = new Body1Typography { FontFamily = font, FontSize = ".9375rem", FontWeight = "400", LineHeight = "1.65", LetterSpacing = "normal" },
        Body2 = new Body2Typography { FontFamily = font, FontSize = ".875rem", FontWeight = "400", LineHeight = "1.6", LetterSpacing = "normal" },
        Button = new ButtonTypography { FontFamily = font, FontSize = ".875rem", FontWeight = "500", LineHeight = "1.75", LetterSpacing = "normal", TextTransform = "none" },
        Caption = new CaptionTypography { FontFamily = font, FontSize = ".75rem", FontWeight = "400", LineHeight = "1.6", LetterSpacing = "normal" },
        Overline = new OverlineTypography { FontFamily = font, FontSize = ".75rem", FontWeight = "600", LineHeight = "1.6", LetterSpacing = "normal", TextTransform = "none" },
    };

    /// <summary>Flat surfaces with hairline borders; elevation only for floating layers (menus, dialogs).</summary>
    private static Shadow SoftShadows()
    {
        var shadows = new Shadow();
        shadows.Elevation[1] = "0 1px 2px rgba(16,24,40,.05)";
        shadows.Elevation[2] = "0 1px 3px rgba(16,24,40,.08), 0 1px 2px rgba(16,24,40,.04)";
        shadows.Elevation[3] = "0 4px 8px -2px rgba(16,24,40,.08), 0 2px 4px -2px rgba(16,24,40,.04)";
        shadows.Elevation[4] = "0 8px 16px -4px rgba(16,24,40,.10), 0 3px 6px -3px rgba(16,24,40,.05)";
        for (var i = 5; i < shadows.Elevation.Length; i++)
            shadows.Elevation[i] = "0 20px 32px -8px rgba(16,24,40,.16), 0 6px 12px -6px rgba(16,24,40,.08)";
        return shadows;
    }
}
