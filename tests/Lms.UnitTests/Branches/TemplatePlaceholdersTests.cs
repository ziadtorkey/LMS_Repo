using Lms.Infrastructure.Branches;

namespace Lms.UnitTests.Branches;

public sealed class TemplatePlaceholdersTests
{
    private static readonly Dictionary<string, string> Map = new() { ["2026"] = "{Year}" };

    [Fact]
    public void Parameterize_then_apply_restores_text()
    {
        const string text = "Section 1 2026 - مرحلة 2026";

        var template = TemplatePlaceholders.Parameterize(text, Map);
        var restored = TemplatePlaceholders.Apply(template, new Dictionary<string, string> { ["Year"] = "2026" });

        Assert.Equal("Section 1 {Year} - مرحلة {Year}", template);
        Assert.Equal(text, restored);
    }

    [Fact]
    public void Apply_substitutes_other_values()
    {
        Assert.Equal("S-2027", TemplatePlaceholders.Apply("S-{Year}", new Dictionary<string, string> { ["Year"] = "2027" }));
    }

    [Fact]
    public void Find_returns_distinct_tokens()
    {
        var found = TemplatePlaceholders.Find("{Year} {Term} {Year} plain {1bad} {}");

        Assert.Equal(new[] { "Term", "Year" }, found.Order());
    }

    [Fact]
    public void Apply_leaves_text_without_tokens_untouched()
    {
        const string text = "Plain text with { braces } only";

        Assert.Equal(text, TemplatePlaceholders.Apply(text, new Dictionary<string, string> { ["Year"] = "2027" }));
        Assert.Equal(text, TemplatePlaceholders.Parameterize(text, new Dictionary<string, string>()));
    }

    [Fact]
    public void Parameterize_prefers_longer_literals()
    {
        var map = new Dictionary<string, string> { ["2026"] = "{Year}", ["2026-27"] = "{Span}" };

        Assert.Equal("{Span} / {Year}", TemplatePlaceholders.Parameterize("2026-27 / 2026", map));
    }

    [Theory]
    [InlineData("{Year}", true)]
    [InlineData("Year", false)]
    [InlineData("{Year} x", false)]
    [InlineData("{}", false)]
    public void IsToken_accepts_a_single_token_only(string text, bool expected) =>
        Assert.Equal(expected, TemplatePlaceholders.IsToken(text));
}
