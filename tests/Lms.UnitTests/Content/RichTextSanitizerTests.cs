using Lms.Domain.Content;
using Lms.Infrastructure.Content;
using Lms.Shared.Content;

namespace Lms.UnitTests.Content;

public sealed class RichTextSanitizerTests
{
    private readonly RichTextSanitizer _sanitizer = new();

    [Theory]
    [InlineData("<p>a<script>alert(1)</script>b</p>", "<p>ab</p>")]
    [InlineData("<p onclick=\"x()\" onmouseover=\"y()\">a</p>", "<p>a</p>")]
    [InlineData("<p style=\"color:red\">a</p>", "<p>a</p>")]
    [InlineData("<p>a</p><iframe src=\"https://evil.example\"></iframe>", "<p>a</p>")]
    [InlineData("<p>a</p><img src=\"x\" onerror=\"x()\">", "<p>a</p>")]
    [InlineData("<style>p{display:none}</style><p>a</p>", "<p>a</p>")]
    [InlineData("<object data=\"x\"></object><embed src=\"x\"><p>a</p>", "<p>a</p>")]
    public void Strips_script_event_handlers_styles_frames_and_images(string input, string expected) =>
        Assert.Equal(expected, _sanitizer.Sanitize(input));

    [Theory]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html;base64,PHNjcmlwdD4=\">x</a>")]
    [InlineData("<a href=\"vbscript:msgbox(1)\">x</a>")]
    public void Drops_javascript_data_and_other_unsafe_urls(string input)
    {
        var clean = _sanitizer.Sanitize(input)!;

        Assert.DoesNotContain("javascript", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", clean);
        Assert.DoesNotContain("vbscript", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", clean);
    }

    [Fact]
    public void Keeps_lists_headings_emphasis_quotes_and_rtl_direction()
    {
        const string html = "<h2>T</h2><h3>S</h3><ul><li>a</li></ul><ol><li>b</li></ol><blockquote>q</blockquote>"
            + "<p class=\"ql-align-center ql-direction-rtl\" dir=\"rtl\"><strong>b</strong><em>i</em><u>u</u><s>s</s>"
            + "<span dir=\"ltr\">x</span></p><p><br></p>";

        Assert.Equal(html, _sanitizer.Sanitize(html));
    }

    [Fact]
    public void Drops_classes_and_direction_values_outside_the_allow_list()
    {
        var clean = _sanitizer.Sanitize("<p class=\"ql-align-right evil-class\" dir=\"sideways\">a</p>");

        Assert.Equal("<p class=\"ql-align-right\">a</p>", clean);
    }

    [Fact]
    public void Links_keep_http_https_and_mailto_and_get_noopener_and_a_new_tab()
    {
        Assert.Equal(
            "<a href=\"https://example.com/a?b=1\" rel=\"noopener noreferrer\" target=\"_blank\">x</a>",
            _sanitizer.Sanitize("<a href=\"https://example.com/a?b=1\" rel=\"opener\" target=\"_self\">x</a>"));
        Assert.Contains("mailto:a@example.com", _sanitizer.Sanitize("<a href=\"mailto:a@example.com\">m</a>"));
        Assert.Contains("noopener noreferrer", _sanitizer.Sanitize("<a href=\"http://example.com\">h</a>"));
        Assert.DoesNotContain("rel=", _sanitizer.Sanitize("<p rel=\"x\">a</p>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<script>alert(1)</script>")]
    public void Nothing_left_is_null(string? input) => Assert.Null(_sanitizer.Sanitize(input));

    [Fact]
    public void Domain_constants_match_the_contract_catalogs()
    {
        Assert.Equal(ModuleVisibilities.Published, LessonModule.PublishedVisibility);
        Assert.Equal(ModuleVisibilities.Scheduled, LessonModule.ScheduledVisibility);
        Assert.Equal(ModuleTargets.ClassCourse, LessonModule.ClassCourseTarget);
        Assert.Equal(CompletionRules.None, LessonModule.NoCompletion);
        Assert.Equal(CompletionStates.NotStarted, ModuleCompletion.NotStartedState);
        Assert.Equal(CompletionStates.InProgress, ModuleCompletion.InProgressState);
        Assert.Equal(CompletionStates.Completed, ModuleCompletion.CompletedState);
    }
}
