using Lms.Domain.LiveSessions;
using Lms.Infrastructure.LiveSessions;

namespace Lms.UnitTests.LiveSessions;

/// <summary>The fake provider behind live sessions until the Zoom provider is registered (M4b Task 4 seam).</summary>
public sealed class FakeMeetingProviderTests
{
    private static MeetingRequest Request(string topic = "Live") => new(
        Guid.NewGuid(), topic, new DateTime(2026, 12, 6, 9, 0, 0, DateTimeKind.Utc), 60, "UTC", Guid.NewGuid(), Guid.NewGuid(), false);

    [Fact]
    public async Task Create_update_and_delete_keep_the_meetings_and_record_the_calls()
    {
        var provider = new FakeMeetingProvider();

        var meeting = await provider.CreateMeetingAsync(Request("A"));
        await provider.UpdateMeetingAsync(meeting.ExternalMeetingId, Request("B"));

        Assert.Equal("B", provider.Meetings[meeting.ExternalMeetingId].Topic);
        Assert.Equal([meeting.ExternalMeetingId], provider.Updated.ToArray());
        await provider.DeleteMeetingAsync(meeting.ExternalMeetingId);
        Assert.Empty(provider.Meetings);
        Assert.Equal([meeting.ExternalMeetingId], provider.Deleted.ToArray());
    }

    [Fact]
    public async Task FailNext_fails_that_many_calls_with_the_given_kind_then_recovers()
    {
        var provider = new FakeMeetingProvider();
        provider.FailNext(2, MeetingFailure.CapacityExhausted);

        var first = await Assert.ThrowsAsync<MeetingProviderException>(() => provider.CreateMeetingAsync(Request()));
        var second = await Assert.ThrowsAsync<MeetingProviderException>(() => provider.DeleteMeetingAsync("x"));
        var third = await provider.CreateMeetingAsync(Request());

        Assert.Equal(MeetingFailure.CapacityExhausted, first.Failure);
        Assert.Equal(30, second.RetryAfterSeconds);
        Assert.NotNull(third.JoinUrl);
        Assert.Empty(provider.Deleted); // a failed delete deletes nothing
    }

    [Fact]
    public async Task Health_is_configured_and_healthy_and_there_are_no_participants_or_recordings()
    {
        var provider = new FakeMeetingProvider();

        Assert.Equal(new ProviderHealth(true, true), await provider.GetHealthAsync());
        Assert.Empty(await provider.GetParticipantsAsync("x"));
        Assert.Empty(await provider.GetRecordingsAsync("x"));
    }
}
