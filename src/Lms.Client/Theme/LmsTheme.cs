using MudBlazor;

namespace Lms.Client.Theme;

/// <summary>
/// The LMS look, from the Stitch design system "Serene Academic Emerald" (docs/design/stitch, owner decision #553):
/// emerald #00663D on a slate canvas, white cards with hairline borders and a micro-shadow, Cairo headings with
/// Inter (en) or Cairo (ar) text. See docs/DESIGN.md. Colours meet WCAG AA in both palettes (LmsThemeTests).
/// Tokens the theme cannot express live in wwwroot/css/lms-theme.css (keep the font stacks there in sync).
/// </summary>
public static class LmsTheme
{
    /// <summary>Arabic UI: Cairo for Arabic and its own Latin glyphs.</summary>
    public static readonly string[] ArabicFontStack =
        ["Cairo", "Segoe UI", "Tahoma", "Arial", "sans-serif"];

    /// <summary>English UI text: Inter; Cairo stays in the stack for Arabic names.</summary>
    public static readonly string[] EnglishFontStack =
        ["Inter", "Segoe UI", "Cairo", "Arial", "sans-serif"];

    /// <summary>Headings use Cairo in both languages (Stitch headline tokens), Inter as the Latin fallback.</summary>
    public static readonly string[] HeadingFontStack =
        ["Cairo", "Inter", "Segoe UI", "Arial", "sans-serif"];

    public static MudTheme Create(bool rightToLeft) => new()
    {
        PaletteLight = Light(),
        PaletteDark = Dark(),
        Typography = Type(rightToLeft ? ArabicFontStack : EnglishFontStack),
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "260px",
            DrawerWidthRight = "260px",
            DrawerMiniWidthLeft = "72px",
            DrawerMiniWidthRight = "72px",
            AppbarHeight = "64px",
        },
        Shadows = Elevation(),
    };

    private static PaletteLight Light() => new()
    {
        Primary = "#00663D",          // institutional emerald: 7.1:1 on white
        PrimaryDarken = "#004C2C",
        PrimaryLighten = "#0E6C42",
        PrimaryContrastText = "#FFFFFF",
        Secondary = "#3E4C5E",        // slate
        SecondaryContrastText = "#FFFFFF",
        Tertiary = "#047857",         // accessible emerald for text; vivid #10B981 is for dots only (lms-theme.css)
        TertiaryContrastText = "#FFFFFF",
        Info = "#1E40AF",
        InfoContrastText = "#FFFFFF",
        Success = "#1D7344",
        SuccessContrastText = "#FFFFFF",
        Warning = "#B45309",
        WarningContrastText = "#FFFFFF",
        Error = "#B42318",
        ErrorContrastText = "#FFFFFF",
        Dark = "#0B1C30",
        Background = "#F8FAFC",       // canvas (level 0)
        BackgroundGray = "#F1F5F9",   // table header, soft panels
        Surface = "#FFFFFF",          // cards, sidebar (level 1)
        AppbarBackground = "rgba(248,250,252,0.86)",
        AppbarText = "#0B1C30",
        DrawerBackground = "#FFFFFF",
        DrawerText = "#0B1C30",
        DrawerIcon = "#475569",
        TextPrimary = "#0B1C30",
        TextSecondary = "#475569",
        TextDisabled = "rgba(11,28,48,0.42)",
        ActionDefault = "#475569",
        ActionDisabled = "rgba(11,28,48,0.32)",
        ActionDisabledBackground = "rgba(11,28,48,0.08)",
        LinesDefault = "#E2E8F0",
        LinesInputs = "#7A8793",      // 3.7:1 on white: control borders need 3:1 (WCAG 1.4.11)
        TableLines = "#F1F5F9",
        TableHover = "#F8FAFC",
        TableStriped = "rgba(11,28,48,0.02)",
        Divider = "#E2E8F0",
        DividerLight = "#F1F5F9",
        HoverOpacity = 0.06,
        Skeleton = "rgba(11,28,48,0.08)",
        OverlayDark = "rgba(9,13,16,0.45)",
    };

    private static PaletteDark Dark() => new()
    {
        Primary = "#4CC38A",          // emerald for dark surfaces: 8:1 on #121A20
        PrimaryDarken = "#36A874",
        PrimaryLighten = "#84D8A5",
        PrimaryContrastText = "#032013",
        Secondary = "#94A3B8",
        SecondaryContrastText = "#0B1220",
        Tertiary = "#34D399",
        TertiaryContrastText = "#022C1E",
        Info = "#60A5FA",
        InfoContrastText = "#0B1B2C",
        Success = "#34D399",
        SuccessContrastText = "#022C1E",
        Warning = "#FBBF24",
        WarningContrastText = "#231500",
        Error = "#F87171",
        ErrorContrastText = "#2A0603",
        Dark = "#05080A",
        Black = "#05080A",
        Background = "#090D10",       // canvas (level 0)
        BackgroundGray = "#16202A",   // table header, soft panels
        Surface = "#121A20",          // cards, sidebar (level 1)
        AppbarBackground = "rgba(9,13,16,0.86)",
        AppbarText = "#E2E8F0",
        DrawerBackground = "#121A20",
        DrawerText = "#E2E8F0",
        DrawerIcon = "#94A3B8",
        TextPrimary = "#E2E8F0",
        TextSecondary = "#94A3B8",
        TextDisabled = "rgba(226,232,240,0.4)",
        ActionDefault = "#94A3B8",
        ActionDisabled = "rgba(226,232,240,0.3)",
        ActionDisabledBackground = "rgba(226,232,240,0.1)",
        LinesDefault = "#1E293B",
        LinesInputs = "#6A7B85",      // 3.9:1 on the dark surface
        TableLines = "#1E293B",
        TableHover = "#1A242D",
        TableStriped = "rgba(226,232,240,0.02)",
        Divider = "#1E293B",
        DividerLight = "#16202A",
        HoverOpacity = 0.08,
        Skeleton = "rgba(226,232,240,0.08)",
        OverlayDark = "rgba(0,0,0,0.6)",
    };

    private static Typography Type(string[] font)
    {
        var head = HeadingFontStack;
        return new Typography
        {
            Default = new DefaultTypography { FontFamily = font, FontSize = ".9375rem", FontWeight = "400", LineHeight = "1.47", LetterSpacing = "normal" },
            H1 = new H1Typography { FontFamily = head, FontSize = "2.5rem", FontWeight = "700", LineHeight = "1.2", LetterSpacing = "-.02em" },
            H2 = new H2Typography { FontFamily = head, FontSize = "2.25rem", FontWeight = "700", LineHeight = "1.25", LetterSpacing = "-.02em" },
            H3 = new H3Typography { FontFamily = head, FontSize = "1.75rem", FontWeight = "600", LineHeight = "1.3", LetterSpacing = "-.01em" },
            H4 = new H4Typography { FontFamily = head, FontSize = "2rem", FontWeight = "700", LineHeight = "1.25", LetterSpacing = "-.02em" },     // page title (headline-xl)
            H5 = new H5Typography { FontFamily = head, FontSize = "1.5rem", FontWeight = "600", LineHeight = "1.33", LetterSpacing = "-.01em" },   // headline-lg
            H6 = new H6Typography { FontFamily = head, FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.45", LetterSpacing = "normal" }, // section title (headline-md)
            Subtitle1 = new Subtitle1Typography { FontFamily = font, FontSize = "1rem", FontWeight = "500", LineHeight = "1.5", LetterSpacing = "normal" },
            Subtitle2 = new Subtitle2Typography { FontFamily = font, FontSize = ".8125rem", FontWeight = "500", LineHeight = "1.38", LetterSpacing = ".01em" }, // label-default
            Body1 = new Body1Typography { FontFamily = font, FontSize = ".9375rem", FontWeight = "400", LineHeight = "1.47", LetterSpacing = "normal" },
            Body2 = new Body2Typography { FontFamily = font, FontSize = ".875rem", FontWeight = "400", LineHeight = "1.43", LetterSpacing = "normal" },
            Button = new ButtonTypography { FontFamily = font, FontSize = ".875rem", FontWeight = "500", LineHeight = "1.43", LetterSpacing = "normal", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = font, FontSize = ".75rem", FontWeight = "400", LineHeight = "1.33", LetterSpacing = "normal" },
            Overline = new OverlineTypography { FontFamily = font, FontSize = ".75rem", FontWeight = "600", LineHeight = "1.33", LetterSpacing = ".02em", TextTransform = "none" },
        };
    }

    /// <summary>
    /// Stitch depth tiers: level 1 (cards) = hairline border plus a micro-shadow; level 2 (menus, popovers, dialogs) = a diffused shadow.
    /// </summary>
    private static Shadow Elevation()
    {
        var shadows = new Shadow();
        shadows.Elevation[1] = "0 1px 3px 0 rgba(0,0,0,.04), 0 1px 2px -1px rgba(0,0,0,.02)";
        shadows.Elevation[2] = "0 1px 3px 0 rgba(0,0,0,.06), 0 1px 2px -1px rgba(0,0,0,.04)";
        shadows.Elevation[3] = "0 4px 10px -2px rgba(0,0,0,.06), 0 2px 4px -2px rgba(0,0,0,.04)";
        for (var i = 4; i < shadows.Elevation.Length; i++)
            shadows.Elevation[i] = "0 10px 25px -5px rgba(0,0,0,.08), 0 8px 10px -6px rgba(0,0,0,.04)";
        return shadows;
    }
}
