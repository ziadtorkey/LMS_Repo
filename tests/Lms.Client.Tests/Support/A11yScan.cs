using AngleSharp.Dom;
using Bunit;

namespace Lms.Client.Tests.Support;

/// <summary>
/// An axe-style scan of rendered markup for the rules a bUnit render can judge (X-10): every control has an accessible name, every image has alt text,
/// there is one h1, tables that stack on a phone label their cells, and nothing is hidden from assistive technology while still focusable.
/// It returns the problems as text so a test can show all of them at once.
/// </summary>
public static class A11yScan
{
    public static IReadOnlyList<string> Problems(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> cut, bool requireOneHeading = true)
    {
        var root = cut.Nodes.OfType<IElement>().ToList();
        var problems = new List<string>();
        var all = root.SelectMany(e => new[] { e }.Concat(e.QuerySelectorAll("*"))).ToList();

        foreach (var button in all.Where(e => e.TagName == "BUTTON" || e.GetAttribute("role") == "button" || (e.TagName == "A" && e.HasAttribute("href"))))
        {
            if (!HasName(button)) problems.Add($"control without an accessible name: {Describe(button)}");
        }

        foreach (var img in all.Where(e => e.TagName == "IMG"))
        {
            if (!img.HasAttribute("alt")) problems.Add($"image without alt: {Describe(img)}");
        }

        foreach (var input in all.Where(e => e.TagName is "INPUT" or "SELECT" or "TEXTAREA" && e.GetAttribute("type") is not ("hidden" or "file")))
        {
            if (!HasInputName(input, all)) problems.Add($"field without a label: {Describe(input)}");
        }

        foreach (var hidden in all.Where(e => e.GetAttribute("aria-hidden") == "true"))
        {
            if (hidden.QuerySelector("button, a[href], input, select, textarea, [tabindex]:not([tabindex='-1'])") is { } focusable)
            {
                problems.Add($"focusable control inside aria-hidden: {Describe(focusable)}");
            }
        }

        foreach (var table in all.Where(e => e.ClassList.Contains("lms-stack-table")))
        {
            foreach (var td in table.QuerySelectorAll("tbody td"))
            {
                if (!td.HasAttribute("data-label") && !td.ClassList.Contains("lms-cell-actions")) problems.Add($"stacked table cell without data-label: {Describe(td)}");
            }
        }

        if (requireOneHeading)
        {
            var h1 = all.Count(e => e.TagName == "H1");
            if (h1 != 1) problems.Add($"expected one h1, found {h1}");
        }

        return problems;
    }

    private static bool HasName(IElement e) =>
        !string.IsNullOrWhiteSpace(e.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(e.GetAttribute("aria-labelledby"))
        || !string.IsNullOrWhiteSpace(e.GetAttribute("title")) || !string.IsNullOrWhiteSpace(e.TextContent) || e.QuerySelector("img[alt]:not([alt=''])") is not null;

    private static bool HasInputName(IElement input, IReadOnlyList<IElement> all)
    {
        if (!string.IsNullOrWhiteSpace(input.GetAttribute("aria-label")) || !string.IsNullOrWhiteSpace(input.GetAttribute("aria-labelledby"))) return true;
        if (input.Closest("label") is not null) return true;
        var id = input.Id;
        if (!string.IsNullOrEmpty(id) && all.Any(e => e.TagName == "LABEL" && e.GetAttribute("for") == id)) return true;
        // a MudBlazor input is named by its label sibling inside the same input control
        return input.Closest(".mud-input-control")?.QuerySelector("label") is not null;
    }

    private static string Describe(IElement e)
    {
        var testId = e.GetAttribute("data-testid");
        var text = (e.TextContent ?? "").Trim();
        if (text.Length > 30) text = text[..30];
        return $"<{e.TagName.ToLowerInvariant()}{(string.IsNullOrEmpty(e.Id) ? "" : " id=" + e.Id)}{(testId is null ? "" : " data-testid=" + testId)}> \"{text}\"";
    }
}
