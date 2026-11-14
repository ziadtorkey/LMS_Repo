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
    public void Fixed_stat_tiles_cap_their_width_from_600px_and_beat_the_stretching_default() =>
        Assert.Matches(new Regex(@"@media \(min-width:\s*600px\)\s*\{\s*\.lms-stats\.lms-stats-fixed\s*\{\s*grid-template-columns:\s*repeat\(auto-fill,\s*minmax\(200px,\s*280px\)\)"), Css());

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
    public void Field_labels_have_room_below_the_line_so_letters_are_not_cut_off()
    {
        // MudBlazor clips a label to its 1.15 line height (overflow: hidden, for the ellipsis), which cuts the dots and tails of Arabic letters in Cairo (6 px) and English descenders (1 px).
        // Its own reset is (0,3,0), so the rule must be at least as specific to win.
        Assert.Matches(new Regex(@"html \.mud-input-control > \.mud-input-control-input-container > \.mud-input-label-inputcontrol\s*\{[^}]*padding-block-end:\s*\d+px"), Css());
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
