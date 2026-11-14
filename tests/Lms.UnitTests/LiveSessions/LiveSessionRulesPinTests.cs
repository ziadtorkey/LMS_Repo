using Lms.Domain.LiveSessions;
using Lms.Shared.LiveSessions;

namespace Lms.UnitTests.LiveSessions;

/// <summary>The domain keeps its own copy of the contract values (it does not reference Lms.Shared); this pins them.</summary>
public sealed class LiveSessionRulesPinTests
{
    [Fact]
    public void Domain_values_equal_the_contract_values()
    {
        Assert.Equal(LiveSessionLimits.MaxOccurrences, LiveSessionRules.MaxOccurrences);
        Assert.Equal(LiveSessionLimits.MinIntervalWeeks, LiveSessionRules.MinIntervalWeeks);
        Assert.Equal(LiveSessionStatuses.Scheduled, LiveSessionRules.Scheduled);
        Assert.Equal(LiveSessionStatuses.Live, LiveSessionRules.Live);
        Assert.Equal(LiveSessionStatuses.Ended, LiveSessionRules.Ended);
        Assert.Equal(LiveSessionStatuses.Cancelled, LiveSessionRules.Cancelled);
        Assert.Equal(LiveSessionStatuses.SyncFailed, LiveSessionRules.SyncFailed);
        Assert.Equal(RecordingSources.Provider, LiveSessionRules.RecordingProvider);
        Assert.Equal(RecordingSources.Manual, LiveSessionRules.RecordingManual);
        Assert.Equal(ParticipantMatchStates.Matched, LiveSessionRules.Matched);
        Assert.Equal(ParticipantMatchStates.Guest, LiveSessionRules.Guest);
        Assert.Equal(RecurrenceDays.All, Enum.GetNames<DayOfWeek>());
    }
}
