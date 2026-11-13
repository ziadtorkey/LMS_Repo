using System.Text.RegularExpressions;

namespace Lms.Client.Tests.Layout;

/// <summary>
/// Guards the M4b presentation classes (design spec docs/design/m4b-live-and-packages.md §6): the stylesheet is linked,
/// defines every class the spec names, and stays RTL-safe and themeable (logical properties, palette variables, no hex
/// colours except the package frame, which is white on purpose).
/// </summary>
public sealed partial class ActivitiesStylesheetTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Lms.Client"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine([dir!, "src", "Lms.Client", .. parts]));
    }

    private static string Css() => RepoFile("wwwroot", "css", "lms-activities.css");

    [Fact]
    public void Stylesheet_is_linked_after_the_theme()
    {
        var index = RepoFile("wwwroot", "index.html");
        var theme = index.IndexOf("css/lms-theme.css", StringComparison.Ordinal);
        var activities = index.IndexOf("css/lms-activities.css", StringComparison.Ordinal);

        Assert.True(theme >= 0 && activities > theme, "lms-activities.css must be linked after lms-theme.css");
    }

    [Theory]
    [InlineData("lms-live-dot")]
    [InlineData("lms-when")]
    [InlineData("lms-host-hint")]
    [InlineData("lms-recurrence-preview")]
    [InlineData("lms-day-toggles")]
    [InlineData("lms-choice")]
    [InlineData("lms-kind-tile")]
    [InlineData("lms-join-panel")]
    [InlineData("lms-date-block")]
    [InlineData("lms-steps")]
    [InlineData("lms-dropzone")]
    [InlineData("lms-file-row")]
    [InlineData("lms-player")]
    [InlineData("lms-player-bar")]
    [InlineData("lms-player-stage")]
    [InlineData("lms-player-frame")]
    [InlineData("lms-player-overlay")]
    [InlineData("lms-preview-strip")]
    [InlineData("lms-save-state")]
    public void Spec_class_is_defined(string name) =>
        Assert.Matches(new Regex($@"\.{Regex.Escape(name)}(?![\w-])"), Css());

    [Fact]
    public void Uses_logical_properties_only()
    {
        var physical = PhysicalProperty().Matches(Css()).Select(m => m.Value).ToList();
        Assert.Empty(physical);
    }

    [Fact]
    public void Colours_come_from_the_palette()
    {
        // The only literal colour is the package frame background: packages are authored for a white page in both themes.
        var hex = HexColour().Matches(Css()).Select(m => m.Value).Where(v => !v.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(hex);
    }

    [Fact]
    public void Player_overlay_covers_only_the_stage() =>
        Assert.Matches(new Regex(@"\.lms-player-overlay\s*\{[^}]*inset:\s*0;"), Css());

    [Fact]
    public void Course_tabs_scroll_in_their_own_row_instead_of_wrapping_or_clipping()
    {
        var css = Css();
        Assert.Matches(new Regex(@"\.lms-course-tabs\s*\{[^}]*flex-wrap:\s*nowrap;[^}]*overflow-x:\s*auto;"), css);
        Assert.Matches(new Regex(@"\.lms-course-tabs \.mud-button-root\s*\{[^}]*flex:\s*none;[^}]*white-space:\s*nowrap;"), css);
    }

    [Fact]
    public void Live_dot_does_not_pulse_under_reduced_motion() =>
        Assert.Matches(new Regex(@"prefers-reduced-motion:\s*reduce\)\s*\{[^}]*\.lms-live-dot\s*\{\s*animation:\s*none"), Css());

    [GeneratedRegex(@"(?<![\w-])(margin|padding|border)-(left|right)\b|(?<![\w-])(left|right)\s*:|text-align:\s*(left|right)|float:\s*(left|right)")]
    private static partial Regex PhysicalProperty();

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b")]
    private static partial Regex HexColour();
}
