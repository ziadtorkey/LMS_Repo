using System.Text.RegularExpressions;

namespace Lms.Client.Tests.Layout;

/// <summary>
/// Guards the Stitch foundation layer (wwwroot/css/lms-stitch.css, #553): linked between the theme and the activity styles,
/// RTL-safe, sentence case only, and colours from the palette except the few named Stitch constants.
/// </summary>
public sealed partial class StitchStylesheetTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine([dir!, "src", "Lms.Client", .. parts]));
    }

    private static string Css() => RepoFile("wwwroot", "css", "lms-stitch.css");

    [Fact]
    public void Stitch_layer_is_linked_after_the_theme_and_before_the_activity_styles()
    {
        var index = RepoFile("wwwroot", "index.html");
        int At(string file) => index.IndexOf(file, StringComparison.Ordinal);

        Assert.True(At("css/lms-theme.css") >= 0 && At("css/lms-stitch.css") > At("css/lms-theme.css"));
        Assert.True(At("css/lms-activities.css") > At("css/lms-stitch.css"));
    }

    [Fact]
    public void Uses_logical_properties_only() =>
        Assert.Empty(PhysicalProperty().Matches(Css()).Select(m => m.Value));

    [Fact]
    public void Never_uppercases_text() =>
        Assert.DoesNotMatch(new Regex(@"text-transform:\s*uppercase"), Css());

    [Fact]
    public void Literal_colours_are_only_the_named_stitch_constants()
    {
        string[] allowed = ["#10B981", "#EFF4FF", "#16202A", "#090D10", "#334155"];
        var hex = HexColour().Matches(Css()).Select(m => m.Value.ToUpperInvariant()).Where(v => !allowed.Contains(v)).ToList();
        Assert.Empty(hex);
    }

    [Fact]
    public void The_two_pane_inbox_fits_under_the_page_header_so_the_page_never_scrolls()
    {
        // Measured: the inbox starts 176 px down and the page keeps 48 px below it; 100dvh - 190px scrolled the page by 34 px at 1440 and 1024.
        Assert.Matches(new Regex(@"\.lms-inbox \{[^}]*block-size: calc\(100dvh - 224px\)"), Css());
    }

    [Fact]
    public void Focus_is_a_visible_ring_not_only_a_glow() =>
        Assert.Matches(new Regex(@"--lms-focus-ring:\s*0 0 0 2px var\(--mud-palette-surface\), 0 0 0 4px var\(--mud-palette-primary\)"), Css());

    [Fact]
    public void Vivid_emerald_is_never_a_text_colour() =>
        Assert.DoesNotMatch(new Regex(@"(?<![-\w])color:\s*var\(--lms-accent\)"), Css());

    [GeneratedRegex(@"(?<![\w-])(margin|padding|border)-(left|right)\b|(?<![\w-])(left|right)\s*:|text-align:\s*(left|right)|float:\s*(left|right)")]
    private static partial Regex PhysicalProperty();

    [GeneratedRegex(@"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b")]
    private static partial Regex HexColour();
}
