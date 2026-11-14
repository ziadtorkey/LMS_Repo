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
    public void Focus_is_a_visible_ring_not_only_a_glow() =>
        Assert.Matches(new Regex(@"--lms-focus-ring:\s*0 0 0 2px var\(--mud-palette-surface\), 0 0 0 4px var\(--mud-palette-primary\)"), Css());

    [Fact]
    public void Vivid_emerald_is_never_a_text_colour() =>
        Assert.DoesNotMatch(new Regex(@"(?<![-\w])color:\s*var\(--lms-accent\)"), Css());

    [Fact]
    public void Half_width_stat_tiles_on_phones_put_the_icon_above_the_value()
    {
        // Two tiles per row at 360 px leave ~150 px each; a long value such as 2026/2027 ran into the icon.
        var phone = Regex.Match(Css(), @"@media \(max-width: 599\.98px\) \{\s*\.lms-stats \{ grid-template-columns: repeat\(2[^@]*?\r?\n\}").Value;
        Assert.Contains("flex-direction: column-reverse", phone);
        Assert.Contains("overflow-wrap: anywhere", phone);
    }

    [GeneratedRegex(@"(?<![\w-])(margin|padding|border)-(left|right)\b|(?<![\w-])(left|right)\s*:|text-align:\s*(left|right)|float:\s*(left|right)")]
    private static partial Regex PhysicalProperty();

    [GeneratedRegex(@"#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b")]
    private static partial Regex HexColour();
}
