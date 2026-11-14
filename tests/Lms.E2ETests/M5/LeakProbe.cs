using Microsoft.Playwright;

namespace Lms.E2ETests.M5;

/// <summary>
/// The whole-page leak check of the grade scenarios (docs/security/m5-review.md section 1): attach it to a page BEFORE navigating, then assert that a marker
/// (a hidden item's title, an unpublished score) appears nowhere: not in the rendered HTML (attributes such as <c>title</c>, <c>aria-*</c> and
/// <c>data-*</c> included, not only the visible text), not in the body or headers of any API response the page received, not in a console message.
/// </summary>
public sealed class LeakProbe
{
    private readonly List<string> _seen = [];
    private readonly IPage _page;

    private LeakProbe(IPage page) => _page = page;

    public static LeakProbe Attach(IPage page)
    {
        var probe = new LeakProbe(page);
        page.Console += (_, m) => probe.Add($"console {m.Type}: {m.Text}");
        page.Response += async (_, r) =>
        {
            if (!r.Url.Contains("/api/", StringComparison.Ordinal))
            {
                return;
            }

            string body;
            try
            {
                body = await r.TextAsync();
            }
            catch (PlaywrightException)
            {
                body = string.Empty; // a redirect or an aborted request has no body
            }

            var headers = string.Join('\n', (await r.AllHeadersAsync()).Select(h => $"{h.Key}: {h.Value}"));
            probe.Add($"{r.Request.Method} {r.Url} {r.Status}\n{headers}\n{body}");
        };
        return probe;
    }

    private void Add(string text)
    {
        lock (_seen)
        {
            _seen.Add(text);
        }
    }

    /// <summary>Fails when <paramref name="marker"/> is in the page HTML or in anything the page received so far.</summary>
    public async Task AssertAbsentAsync(string marker)
    {
        var html = await _page.ContentAsync();
        Assert.DoesNotContain(marker, html, StringComparison.Ordinal);
        string[] seen;
        lock (_seen)
        {
            seen = [.. _seen];
        }

        Assert.DoesNotContain(seen, s => s.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>The API responses seen so far that mention <paramref name="needle"/> (for example the grades call), for a positive check.</summary>
    public IReadOnlyList<string> Mentioning(string needle)
    {
        lock (_seen)
        {
            return [.. _seen.Where(s => s.Contains(needle, StringComparison.Ordinal))];
        }
    }
}
