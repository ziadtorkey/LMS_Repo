using Bunit;
using Lms.Client.Components.Editors;
using Lms.Client.Tests.Support;

namespace Lms.Client.Tests.Components;

public sealed class RichTextHelperTests
{
    [Theory]
    [InlineData("https://example.com/a?b=1", "https://example.com/a?b=1")]
    [InlineData("  http://example.com  ", "http://example.com")]
    [InlineData("mailto:teacher@school.edu", "mailto:teacher@school.edu")]
    [InlineData("example.com/page", "https://example.com/page")]
    [InlineData("www.example.com", "https://www.example.com")]
    public void Normalize_accepts_web_and_mail_links(string input, string expected) =>
        Assert.Equal(expected, RichTextLink.Normalize(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox")]
    [InlineData("//evil.example")]
    [InlineData("has space.com")]
    public void Normalize_rejects_unsafe_or_empty_links(string? input) =>
        Assert.Null(RichTextLink.Normalize(input));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("<p><br></p>", true)]
    [InlineData("<p></p><p> </p>", true)]
    [InlineData("<p>&nbsp;</p>", true)]
    [InlineData("<p>hello</p>", false)]
    [InlineData("<ul><li>x</li></ul>", false)]
    public void IsEmpty_ignores_markup_and_whitespace(string? html, bool expected) =>
        Assert.Equal(expected, RichTextHtml.IsEmpty(html));
}

public sealed class RichTextEditorTests : ClientTestContext
{
    private const string Module = "./js/richtext.js";
    private BunitJSModuleInterop? _js;
    private BunitJSModuleInterop Js() => _js ??= JSInterop.SetupModule(Module);

    private IRenderedComponent<RichTextEditor> Render(Action<ComponentParameterCollectionBuilder<RichTextEditor>>? configure = null)
    {
        var module = Js();
        module.Mode = JSRuntimeMode.Loose;
        return Render<RichTextEditor>(p => { p.Add(c => c.Label, "Lesson text"); configure?.Invoke(p); });
    }

    [Fact]
    public void Initialises_the_editor_with_the_html_value_after_first_render()
    {
        var cut = Render(p => p.Add(c => c.Value, "<p>hi</p>"));

        var init = Js().Invocations["init"].Single();
        Assert.Equal("<p>hi</p>", init.Arguments[2]!.GetType().GetProperty("html")!.GetValue(init.Arguments[2]));
        Assert.NotNull(cut.Find("[data-testid='rte-host']"));
    }

    [Fact]
    public void Toolbar_offers_bold_italic_lists_and_link_but_no_image()
    {
        var cut = Render();

        foreach (var id in new[] { "bold", "italic", "bullet", "ordered", "link" })
            Assert.NotNull(cut.Find($"[data-testid='rte-{id}']"));
        Assert.Empty(cut.FindAll("[data-testid='rte-image']"));
        Assert.Equal("toolbar", cut.Find("[role='toolbar']").GetAttribute("role"));
    }

    [Fact]
    public void Toolbar_labels_are_localised_in_arabic_and_english()
    {
        var ar = Render();
        Assert.Equal("غامق", ar.Find("[data-testid='rte-bold']").GetAttribute("aria-label"));

        Culture.Current = new System.Globalization.CultureInfo("en");
        var en = Render();
        Assert.Equal("Bold", en.Find("[data-testid='rte-bold']").GetAttribute("aria-label"));
        Assert.Equal("Insert link", en.Find("[data-testid='rte-link']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Clicking_a_toolbar_button_applies_that_format_in_the_editor()
    {
        var cut = Render();

        cut.Find("[data-testid='rte-bold']").Click();
        cut.Find("[data-testid='rte-bullet']").Click();
        cut.Find("[data-testid='rte-ordered']").Click();

        var formats = Js().Invocations["format"].Select(i => $"{i.Arguments[1]}:{i.Arguments[2]}").ToArray();
        Assert.Equal(["bold:True", "list:bullet", "list:ordered"], formats);
    }

    [Fact]
    public async Task Html_from_the_editor_flows_out_through_ValueChanged()
    {
        string? received = null;
        var cut = Render(p => p.Add(c => c.ValueChanged, (string v) => received = v));

        await cut.InvokeAsync(() => cut.Instance.OnHtmlChanged("<p><strong>x</strong></p>"));

        Assert.Equal("<p><strong>x</strong></p>", received);
    }

    [Fact]
    public void Changing_Value_from_the_parent_pushes_the_new_html_into_the_editor()
    {
        var cut = Render(p => p.Add(c => c.Value, "<p>a</p>"));

        cut.Render(p => p.Add(c => c.Value, "<p>b</p>"));

        var set = Js().Invocations["setHtml"].Single();
        Assert.Equal("<p>b</p>", set.Arguments[1]);
    }

    [Fact]
    public async Task Echoing_back_the_emitted_value_does_not_reset_the_editor()
    {
        var cut = Render(p => p.Add(c => c.Value, ""));
        await cut.InvokeAsync(() => cut.Instance.OnHtmlChanged("<p>typed</p>"));

        cut.Render(p => p.Add(c => c.Value, "<p>typed</p>"));

        Assert.Empty(Js().Invocations["setHtml"]);
    }

    [Fact]
    public void ReadOnly_disables_the_toolbar_and_the_editor()
    {
        var cut = Render(p => p.Add(c => c.ReadOnly, true));

        Assert.True(cut.Find("[data-testid='rte-bold']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='rte-link']").HasAttribute("disabled"));
        var init = Js().Invocations["init"].Single().Arguments[2]!;
        Assert.Equal(true, init.GetType().GetProperty("readOnly")!.GetValue(init));
    }

    [Fact]
    public async Task Active_formats_reported_by_the_editor_highlight_the_toolbar_buttons()
    {
        var cut = Render();

        await cut.InvokeAsync(() => cut.Instance.OnFormats(new RichTextFormats(Bold: true, Italic: false, List: "bullet", Link: null)));

        Assert.Equal("true", cut.Find("[data-testid='rte-bold']").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid='rte-italic']").GetAttribute("aria-pressed"));
        Assert.Equal("true", cut.Find("[data-testid='rte-bullet']").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid='rte-ordered']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void Link_button_opens_the_url_bar_prefilled_with_the_current_link()
    {
        Js().Setup<RichTextLinkState>("beginLink", _ => true).SetResult(new RichTextLinkState(true, "https://old.example"));
        var cut = Render();

        cut.Find("[data-testid='rte-link']").Click();

        Assert.Equal("https://old.example", cut.Find("[data-testid='rte-link-url']").GetAttribute("value"));
    }

    [Fact]
    public void Applying_a_link_sends_the_normalised_url_and_closes_the_bar()
    {
        Js().Setup<RichTextLinkState>("beginLink", _ => true).SetResult(new RichTextLinkState(true, null));
        var cut = Render();
        cut.Find("[data-testid='rte-link']").Click();

        cut.Find("[data-testid='rte-link-url']").Input("example.com/x");
        cut.Find("[data-testid='rte-link-apply']").Click();

        var apply = Js().Invocations["applyLink"].Single();
        Assert.Equal("https://example.com/x", apply.Arguments[1]);
        Assert.Empty(cut.FindAll("[data-testid='rte-link-url']"));
    }

    [Fact]
    public void An_unsafe_link_is_rejected_with_a_message_and_not_applied()
    {
        Js().Setup<RichTextLinkState>("beginLink", _ => true).SetResult(new RichTextLinkState(true, null));
        var cut = Render();
        cut.Find("[data-testid='rte-link']").Click();

        cut.Find("[data-testid='rte-link-url']").Input("javascript:alert(1)");
        cut.Find("[data-testid='rte-link-apply']").Click();

        Assert.Empty(Js().Invocations["applyLink"]);
        Assert.Contains("رابط", cut.Find("[data-testid='rte-link-bar']").TextContent);
    }

    [Fact]
    public void Removing_a_link_clears_the_link_format()
    {
        Js().Setup<RichTextLinkState>("beginLink", _ => true).SetResult(new RichTextLinkState(true, "https://old.example"));
        var cut = Render();
        cut.Find("[data-testid='rte-link']").Click();

        cut.Find("[data-testid='rte-link-remove']").Click();

        Assert.Null(Js().Invocations["applyLink"].Single().Arguments[1]);
    }

    [Fact]
    public void The_editor_follows_the_ui_direction_by_default()
    {
        Assert.Equal("rtl", Render().Find("[data-testid='rte-host']").GetAttribute("data-default-dir"));

        Culture.Current = new System.Globalization.CultureInfo("en");
        Assert.Equal("ltr", Render().Find("[data-testid='rte-host']").GetAttribute("data-default-dir"));
    }

    [Fact]
    public void The_editor_is_labelled_for_assistive_technology()
    {
        var cut = Render(p => p.Add(c => c.Required, true));

        var label = cut.Find("label.lms-rte-label");
        Assert.Contains("Lesson text", label.TextContent);
        var init = Js().Invocations["init"].Single().Arguments[2]!;
        Assert.Equal(label.Id, init.GetType().GetProperty("labelledBy")!.GetValue(init));
    }

    [Fact]
    public async Task Disposing_releases_the_editor_instance()
    {
        var cut = Render();

        await cut.Instance.DisposeAsync();

        Assert.Single(Js().Invocations["destroy"]);
    }
}
