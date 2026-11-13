using Bunit;
using Lms.Client.Components;
using Lms.Client.Services;
using Lms.Client.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Components;

public sealed class AnnouncerTests : ClientTestContext
{
    [Fact]
    public void The_region_is_a_polite_status_that_shows_the_announced_text()
    {
        var cut = Render<AnnouncerRegion>();
        var region = cut.Find("[data-testid=announcer]");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal("polite", region.GetAttribute("aria-live"));
        Assert.Equal("true", region.GetAttribute("aria-atomic"));

        cut.InvokeAsync(() => Services.GetRequiredService<Announcer>().Announce("Reply posted"));

        Assert.Equal("Reply posted", cut.Find("[data-testid=announcer]").TextContent.Trim());
    }

    [Fact]
    public void The_same_text_twice_is_announced_again()
    {
        var cut = Render<AnnouncerRegion>();
        var announcer = Services.GetRequiredService<Announcer>();
        cut.InvokeAsync(() => announcer.Announce("Saved"));
        var first = cut.Find("[data-testid=announcer] span").GetAttribute("data-n");

        cut.InvokeAsync(() => announcer.Announce("Saved"));

        // A screen reader only reads a live region again when its content is replaced: a new node per announcement.
        Assert.NotEqual(first, cut.Find("[data-testid=announcer] span").GetAttribute("data-n"));
    }

    [Fact]
    public void The_layout_has_the_region_so_every_page_can_announce()
    {
        Culture.Current = new System.Globalization.CultureInfo("en");
        var layout = Render<Lms.Client.Layout.MainLayout>(p => p.Add(l => l.Body, (Microsoft.AspNetCore.Components.RenderFragment)(b => { })));

        Assert.NotEmpty(layout.FindAll("[data-testid=announcer]"));
    }
}
