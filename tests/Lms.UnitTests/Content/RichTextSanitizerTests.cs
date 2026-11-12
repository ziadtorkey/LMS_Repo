using Lms.Domain.Content;
using Lms.Infrastructure.Content;

namespace Lms.UnitTests.Content;

public sealed class RichTextSanitizerTests
{
    private readonly IRichTextSanitizer sut = new RichTextSanitizer();

    [Fact]
    public void Sanitizer_strips_script_event_handlers_javascript_and_data_urls()
    {
        var html = "<p onclick=\"x()\">a</p><script>alert(1)</script>"
            + "<a href=\"javascript:alert(1)\">j</a><a href=\"data:text/html;base64,AAA\">d</a>"
            + "<a href=\"JaVaScRiPt:alert(1)\">m</a><iframe src=\"https://x\"></iframe>"
            + "<style>p{}</style><img src=\"https://x/a.png\" onerror=\"x()\"><svg onload=\"x()\"></svg>";

        var result = sut.Sanitize(html);

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<svg", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>a</p>", result);
    }

    [Fact]
    public void Sanitizer_keeps_lists_headings_links_and_rtl_direction()
    {
        var html = "<h2>t</h2><h3>u</h3><h4>v</h4><ol><li>1</li></ol><ul><li>2</li></ul>"
            + "<blockquote>q</blockquote><p><strong>b</strong><em>i</em><u>u</u><s>s</s><br></p>"
            + "<p dir=\"rtl\" class=\"ql-align-right ql-direction-rtl\">ar</p>"
            + "<a href=\"https://example.com/x\">l</a><a href=\"mailto:a@b.com\">m</a>";

        var result = sut.Sanitize(html);

        foreach (var fragment in new[]
                 {
                     "<h2>t</h2>", "<h3>u</h3>", "<h4>v</h4>", "<ol><li>1</li></ol>", "<ul><li>2</li></ul>",
                     "<blockquote>q</blockquote>", "<strong>b</strong>", "<em>i</em>", "<u>u</u>", "<s>s</s>",
                     "dir=\"rtl\"", "ql-align-right", "ql-direction-rtl", "href=\"https://example.com/x\"", "href=\"mailto:a@b.com\"",
                 })
        {
            Assert.Contains(fragment, result);
        }
    }

    [Fact]
    public void Sanitizer_forces_noopener_on_links()
    {
        var result = sut.Sanitize("<a href=\"https://example.com\" rel=\"opener\" target=\"_self\">x</a>");

        Assert.Contains("rel=\"noopener noreferrer\"", result);
        Assert.Contains("target=\"_blank\"", result);
        Assert.DoesNotContain("opener\"", result.Replace("noopener noreferrer", string.Empty));
    }

    [Fact]
    public void Sanitizer_drops_unlisted_classes_attributes_and_styles()
    {
        var result = sut.Sanitize("<p class=\"evil ql-align-center\" style=\"color:red\" id=\"i\" data-x=\"1\">a</p>");

        Assert.Contains("ql-align-center", result);
        Assert.DoesNotContain("evil", result);
        Assert.DoesNotContain("style", result);
        Assert.DoesNotContain("id=", result);
        Assert.DoesNotContain("data-x", result);
    }

    [Fact]
    public void Sanitizer_treats_blank_input_as_empty()
    {
        Assert.Equal(string.Empty, sut.Sanitize(null));
        Assert.Equal(string.Empty, sut.Sanitize("   "));
    }

    [Fact]
    public void Sanitizer_rejects_a_body_over_the_limit()
    {
        var tooLong = new string('a', IRichTextSanitizer.MaxLength + 1);

        Assert.Throws<RichTextTooLongException>(() => sut.Sanitize(tooLong));
    }

    [Fact]
    public void Sanitizer_is_idempotent()
    {
        var once = sut.Sanitize("<p dir=\"rtl\">x <a href=\"https://e.com\">l</a></p><script>1</script>");

        Assert.Equal(once, sut.Sanitize(once));
    }
}
