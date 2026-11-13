using System.Text;
using Lms.Infrastructure.Packages.Scorm;
using Lms.IntegrationTests.Packages.Corpus;

namespace Lms.IntegrationTests.Packages.Scorm;

/// <summary>The prelude injection (M4b Task 10, Decision 11) on the injection pages of the corpus and on hand-made edge cases.</summary>
public sealed class ScormPreludeInjectorTests
{
    private const string Src = "/p/token/_lms/scorm-prelude.abc123.js";

    private const string Data = "{\"version\":\"Scorm12\",\"model\":{}}";

    private static readonly string Injected = ScormPreludeInjector.Markup(Src, Data);

    private static byte[] CorpusPage(string corpus, string page = "index.html")
    {
        var c = CorpusCases.All.Single(x => x.Name == corpus);
        var view = RawZipView.Parse(File.ReadAllBytes(CorpusCases.CommittedFile(c)));
        return view.ReadData(view.Find(page));
    }

    private static string Text(byte[] bytes) => ScormPreludeInjector.Decode(bytes, out _, out _);

    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    [Fact]
    public void The_prelude_is_the_first_child_of_head()
    {
        var page = Text(ScormPreludeInjector.Inject(Utf8("<!doctype html><html><head><title>t</title></head><body>x</body></html>"), Src, Data));

        Assert.Contains("<head>" + Injected + "<title>t</title>", page);
        Assert.Equal(1, page.Split(Injected).Length - 1);
    }

    [Theory]
    [InlineData("html_utf8_bom")]
    [InlineData("html_meta_charset_latin1")]
    [InlineData("html_utf16le_bom")]
    [InlineData("html_utf16be_bom")]
    [InlineData("html_no_head")]
    [InlineData("html_no_html_no_head")]
    [InlineData("html_doctype_and_comments_first")]
    [InlineData("html_uppercase_and_attributes")]
    [InlineData("html_head_text_in_script")]
    [InlineData("html_conditional_comment")]
    [InlineData("scorm12_valid")]
    [InlineData("sco_catches_the_parent_walk_error")]
    public void Prelude_is_injected_once_into_every_html_page_of_the_corpus(string corpus)
    {
        var original = CorpusPage(corpus);

        var result = ScormPreludeInjector.InjectIfHtml("index.html", original, Src, Data);

        var text = Text(result);
        Assert.Equal(1, text.Split(Injected).Length - 1);
        Assert.True(result.Length > original.Length);
        // Nothing of the original is lost: removing the injected markup (and a head we created) gives the original text back.
        var restored = text.Replace("<head>" + Injected + "</head>", string.Empty).Replace(Injected, string.Empty);
        Assert.Equal(Text(original), restored);
    }

    [Fact]
    public void Injection_handles_a_utf8_bom()
    {
        var original = CorpusPage("html_utf8_bom");
        Assert.Equal([0xEF, 0xBB, 0xBF], original[..3]);

        var result = ScormPreludeInjector.Inject(original, Src, Data);

        Assert.Equal([0xEF, 0xBB, 0xBF], result[..3]);
        Assert.Equal((byte)'<', result[3]);
    }

    [Fact]
    public void Injection_respects_meta_charset_and_utf16_pages()
    {
        var latin1 = CorpusPage("html_meta_charset_latin1");
        var le = CorpusPage("html_utf16le_bom");
        var be = CorpusPage("html_utf16be_bom");

        var latinResult = ScormPreludeInjector.Inject(latin1, Src, Data);
        var leResult = ScormPreludeInjector.Inject(le, Src, Data);
        var beResult = ScormPreludeInjector.Inject(be, Src, Data);

        // The windows-1252 byte 0xE9 of the title survives as that single byte: the page is never round-tripped through UTF-8.
        Assert.Contains((byte)0xE9, latinResult);
        Assert.DoesNotContain((byte)0xC3, latinResult);
        Assert.Equal([0xFF, 0xFE], leResult[..2]);
        Assert.Equal([0xFE, 0xFF], beResult[..2]);
        Assert.Contains(Injected, Encoding.Unicode.GetString(leResult, 2, leResult.Length - 2));
        Assert.Contains(Injected, Encoding.BigEndianUnicode.GetString(beResult, 2, beResult.Length - 2));
    }

    [Fact]
    public void Injection_creates_head_when_missing()
    {
        var withHtml = Text(ScormPreludeInjector.Inject(CorpusPage("html_no_head"), Src, Data));
        var fragment = Text(ScormPreludeInjector.Inject(CorpusPage("html_no_html_no_head"), Src, Data));

        Assert.Contains("<html><head>" + Injected + "</head>", withHtml);
        Assert.StartsWith("<head>" + Injected + "</head><p>fragment</p>", fragment);
    }

    [Fact]
    public void Injection_survives_doctype_and_comments_before_html()
    {
        var page = Text(ScormPreludeInjector.Inject(CorpusPage("html_doctype_and_comments_first"), Src, Data));
        var conditional = Text(ScormPreludeInjector.Inject(CorpusPage("html_conditional_comment"), Src, Data));
        var upper = Text(ScormPreludeInjector.Inject(CorpusPage("html_uppercase_and_attributes"), Src, Data));
        var inScript = Text(ScormPreludeInjector.Inject(CorpusPage("html_head_text_in_script"), Src, Data));

        // The decoy "<head>" in the first comment is skipped: the doctype and comments stay first, the prelude follows the real head.
        Assert.StartsWith("<!-- <head> decoy -->\n<!doctype html>", page);
        Assert.Contains("<html><head>" + Injected + "<title>t</title>", page);
        Assert.Contains("<html><head>" + Injected + "<title>t</title>", conditional);
        Assert.Contains("<HEAD profile=\"x\">" + Injected, upper);
        Assert.Contains("<head>" + Injected + "<script>var s='<head>';</script>", inScript);
    }

    [Fact]
    public void A_head_mentioned_only_in_a_comment_or_attribute_is_not_the_head()
    {
        var page = Text(ScormPreludeInjector.Inject(Utf8("<html><!-- <head> --><body data-x=\"<head>\">b</body></html>"), Src, Data));

        Assert.StartsWith("<html><head>" + Injected + "</head><!-- <head> -->", page);
    }

    [Fact]
    public void Non_html_responses_are_not_modified()
    {
        var js = Utf8("var a = '<head>';");
        var svg = Utf8("<svg><head/></svg>");
        var bytes = new byte[] { 1, 2, 3 };

        Assert.Same(js, ScormPreludeInjector.InjectIfHtml("app.js", js, Src, Data));
        Assert.Same(svg, ScormPreludeInjector.InjectIfHtml("logo.svg", svg, Src, Data));
        Assert.Same(bytes, ScormPreludeInjector.InjectIfHtml("data.bin", bytes, Src, Data));
        Assert.NotSame(js, ScormPreludeInjector.InjectIfHtml("PAGE.HTM", js, Src, Data));
    }

    [Fact]
    public void The_data_block_cannot_close_the_script_element_or_carry_markup()
    {
        var markup = ScormPreludeInjector.Markup(Src, "{\"model\":{\"cmi.suspend_data\":\"</script><script>alert(1)</script><!-- &\"}}");

        Assert.DoesNotContain("<script>alert", markup);
        Assert.DoesNotContain("<!--", markup);
        Assert.Equal(2, markup.Split("</script>").Length - 1);
        Assert.Contains("\\u003c/script\\u003e", markup);
    }

    [Fact]
    public void Initial_state_carries_only_writable_keys_and_never_the_student_name()
    {
        var model = new Dictionary<string, string>
        {
            ["cmi.core.lesson_status"] = "incomplete",
            ["cmi.suspend_data"] = "page=3",
            ["cmi.core.student_name"] = "Ada Lovelace",
            ["cmi.core.student_id"] = "S-1001",
            ["cmi.learner_name"] = "Ada",
            ["cmi.launch_data"] = "x",
        };

        var json = ScormInitialDataBlock.Build("Scorm12", model, "resume", 7);

        Assert.Contains("cmi.core.lesson_status", json);
        Assert.Contains("page=3", json);
        Assert.DoesNotContain("Ada", json);
        Assert.DoesNotContain("S-1001", json);
        Assert.DoesNotContain("cmi.launch_data", json);
        Assert.Contains("\"entry\":\"resume\"", json);
        Assert.Contains("\"seq\":7", json);
    }
}
