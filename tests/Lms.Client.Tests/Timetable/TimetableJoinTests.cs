using System.Globalization;
using Bunit;
using Lms.Client.Components.Timetable;
using Lms.Client.Tests.Support;
using Lms.Shared.Timetable;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Client.Tests.Timetable;

/// <summary>The Join entry on a timetable card (M5 Task 32, BE4 contract: LiveSessionId + JoinOpensAtUtc).</summary>
public sealed class TimetableJoinTests : TimetableTestContext
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Slot = Guid.NewGuid();

    public TimetableJoinTests() => Services.AddSingleton<TimeProvider>(new FixedClock());

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static TimetableCardDto Card(Guid? session, DateTime? opens, string? exception = null) => new(
        Slot, new DateOnly(2026, 11, 17), Guid.NewGuid(), "الرياضيات", "Mathematics", "A. Saleh", null, exception, null, "5أ", "5A", session, opens);

    private IRenderedComponent<TimetableCard> RenderCard(TimetableCardDto card)
    {
        FakeCultureService.Apply(new CultureInfo("en"));
        RenderProviders();
        return Render<TimetableCard>(p => p.Add(x => x.Card, card));
    }

    private string Id => $"{Slot}-2026-11-17";

    [Fact]
    public void A_lesson_without_a_live_session_has_no_join()
    {
        var page = RenderCard(Card(null, null));

        Assert.Empty(page.FindAll($"[data-testid=tt-join-{Id}]"));
    }

    [Fact]
    public void Join_is_disabled_with_the_opening_time_before_the_window()
    {
        var page = RenderCard(Card(Guid.NewGuid(), Now.UtcDateTime.AddMinutes(20)));

        Assert.True(page.Find($"[data-testid=tt-join-{Id}]").HasAttribute("disabled"));
        Assert.Contains("Join opens at", page.Find($"[data-testid=tt-join-hint-{Id}]").TextContent);
    }

    [Fact]
    public void Join_is_enabled_from_the_opening_time()
    {
        var page = RenderCard(Card(Guid.NewGuid(), Now.UtcDateTime));
        var button = page.Find($"[data-testid=tt-join-{Id}]");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Empty(page.FindAll($"[data-testid=tt-join-hint-{Id}]"));
    }

    [Fact]
    public void A_cancelled_lesson_has_no_join()
    {
        var page = RenderCard(Card(Guid.NewGuid(), Now.UtcDateTime, ExceptionKinds.Cancelled));

        Assert.Empty(page.FindAll($"[data-testid=tt-join-{Id}]"));
    }
}
