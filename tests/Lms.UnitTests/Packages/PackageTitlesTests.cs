using Lms.Infrastructure.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>Package titles are bounded to the column and cleaned of hidden characters (QA review of #548 and #557).</summary>
public sealed class PackageTitlesTests
{
    [Fact]
    public void A_very_long_title_is_cut_to_the_column_length()
    {
        var title = PackageTitles.Clean(new string('x', 10_000));

        Assert.Equal(PackageTitles.MaxLength, title!.Length);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var title = PackageTitles.Clean(new string('x', PackageTitles.MaxLength - 1) + "\U0001F600");

        Assert.True(title!.Length <= PackageTitles.MaxLength);
        Assert.DoesNotContain(title, char.IsHighSurrogate);
    }

    [Fact]
    public void Control_and_bidi_characters_are_removed_and_the_rest_trimmed()
    {
        Assert.Equal("Course", PackageTitles.Clean("  Co‮ur\u0001se؜ \n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ‮ \u0001 ")]
    public void Nothing_left_means_no_title(string? input) => Assert.Null(PackageTitles.Clean(input));
}
