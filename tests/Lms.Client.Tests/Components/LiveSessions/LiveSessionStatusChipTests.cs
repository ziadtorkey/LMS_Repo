using System.Globalization;
using Bunit;
using Lms.Client.Components.LiveSessions;
using Lms.Client.Tests.Support;
using Lms.Shared.LiveSessions;

namespace Lms.Client.Tests.Components.LiveSessions;

/// <summary>The live session status chip of the M4b design spec §2.2 (docs/design/m4b-live-and-packages.md).</summary>
public sealed class LiveSessionStatusChipTests : ClientTestContext
{
    [Theory]
    [InlineData(LiveSessionStatuses.Scheduled, "info", "مجدولة")]
    [InlineData(LiveSessionStatuses.Live, "success", "مباشرة الآن")]
    [InlineData(LiveSessionStatuses.Ended, "default", "انتهت")]
    [InlineData(LiveSessionStatuses.Cancelled, "default", "ملغاة")]
    [InlineData(LiveSessionStatuses.SyncFailed, "warning", "بانتظار Zoom")]
    public void Each_status_is_a_soft_chip_with_its_colour_and_arabic_label(string status, string colour, string label)
    {
        var cut = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, status));

        var chip = cut.Find("[data-testid=live-status]");
        Assert.Equal(status, chip.GetAttribute("data-status"));
        Assert.Contains("mud-chip-text", chip.ClassName);
        Assert.Contains($"mud-chip-color-{colour}", chip.ClassName);
        Assert.Equal(label, chip.TextContent.Trim());
    }

    [Theory]
    [InlineData(LiveSessionStatuses.Scheduled, "Scheduled")]
    [InlineData(LiveSessionStatuses.Live, "Live now")]
    [InlineData(LiveSessionStatuses.Ended, "Ended")]
    [InlineData(LiveSessionStatuses.Cancelled, "Cancelled")]
    [InlineData(LiveSessionStatuses.SyncFailed, "Waiting for Zoom")]
    public void Each_status_has_its_english_label(string status, string label)
    {
        FakeCultureService.Apply(new CultureInfo("en"));

        var cut = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, status));

        Assert.Equal(label, cut.Find("[data-testid=live-status]").TextContent.Trim());
    }

    [Fact]
    public void Live_now_carries_the_pulsing_dot_and_the_others_do_not()
    {
        var live = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, LiveSessionStatuses.Live));
        var dot = live.Find(".lms-live-dot");
        Assert.Equal("true", dot.GetAttribute("aria-hidden"));

        foreach (var other in LiveSessionStatuses.All.Where(s => s != LiveSessionStatuses.Live))
        {
            var cut = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, other));
            Assert.Empty(cut.FindAll(".lms-live-dot"));
        }
    }

    [Fact]
    public void Sync_failed_shows_the_explanation_under_the_chip_when_asked()
    {
        var cut = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, LiveSessionStatuses.SyncFailed).Add(x => x.ShowHint, true));

        var hint = cut.Find("[data-testid=live-status-hint]");
        Assert.Contains("سيعيد النظام المحاولة تلقائيًا", hint.TextContent);
    }

    [Theory]
    [InlineData(LiveSessionStatuses.Scheduled, true)]
    [InlineData(LiveSessionStatuses.SyncFailed, false)]
    public void The_explanation_appears_only_for_sync_failed_and_only_when_asked(string status, bool showHint)
    {
        var cut = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, status).Add(x => x.ShowHint, showHint));

        Assert.Empty(cut.FindAll("[data-testid=live-status-hint]"));
    }

    [Fact]
    public void An_unknown_status_falls_back_to_a_neutral_chip_with_its_raw_name()
    {
        var cut = Render<LiveSessionStatusChip>(p => p.Add(x => x.Status, "Paused"));

        var chip = cut.Find("[data-testid=live-status]");
        Assert.Contains("mud-chip-color-default", chip.ClassName);
        Assert.Equal("Paused", chip.TextContent.Trim());
    }
}
