using System.Text;
using Lms.Infrastructure.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>H5P JSON is parsed with a size, depth and item cap (M4b Task 8b).</summary>
public sealed class SafeJsonTests
{
    private static MemoryStream Json(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void A_normal_document_parses()
    {
        var result = SafeJson.Parse(Json("{\"title\":\"x\",\"items\":[1,2,3]}"));

        Assert.Null(result.Problem);
        Assert.Equal("x", result.Document!.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public void Deep_or_huge_json_is_rejected()
    {
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 200)) + "1" + new string('}', 200);
        var huge = "{\"a\":\"" + new string('x', SafeJson.MaxBytes) + "\"}";
        var wide = "[" + string.Join(',', Enumerable.Repeat("1", SafeJson.MaxItems + 1)) + "]";

        Assert.Equal("too_deep", SafeJson.Parse(Json(deep)).Problem);
        Assert.Equal("too_large", SafeJson.Parse(Json(huge)).Problem);
        Assert.Equal("too_many_items", SafeJson.Parse(Json(wide)).Problem);
    }

    [Fact]
    public void The_limits_are_the_documented_ones()
    {
        Assert.Equal((1024 * 1024, 32, 20_000), (SafeJson.MaxBytes, SafeJson.MaxDepth, SafeJson.MaxItems));
        Assert.Null(SafeJson.Parse(Json(string.Concat(Enumerable.Repeat("[", 31)) + string.Concat(Enumerable.Repeat("]", 31)))).Problem);
    }

    [Theory]
    [InlineData("<<not json>>")]
    [InlineData("")]
    [InlineData("{\"a\":")]
    public void Anything_that_is_not_json_is_refused(string text) => Assert.Equal("not_json", SafeJson.Parse(Json(text)).Problem);
}
