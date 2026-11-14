using Lms.Domain.LiveSessions;

namespace Lms.Infrastructure.LiveSessions;

/// <summary>
/// The provider of a host where no meeting provider is configured (the default until Zoom is registered): every call that would
/// reach a provider fails with <see cref="MeetingFailure.NotConfigured"/>, so a create answers 409 <c>provider_not_configured</c>
/// instead of handing out a link that leads nowhere.
/// </summary>
public sealed class NotConfiguredMeetingProvider : IMeetingProvider
{
    public string Name => "None";

    public Task<MeetingResult> CreateMeetingAsync(MeetingRequest request, CancellationToken ct = default) => throw NotConfigured();

    public Task UpdateMeetingAsync(string externalMeetingId, MeetingRequest request, CancellationToken ct = default) => throw NotConfigured();

    public Task DeleteMeetingAsync(string externalMeetingId, CancellationToken ct = default) => throw NotConfigured();

    public Task<IReadOnlyList<MeetingParticipant>> GetParticipantsAsync(string externalMeetingId, CancellationToken ct = default) =>
        throw NotConfigured();

    public Task<IReadOnlyList<MeetingRecording>> GetRecordingsAsync(string externalMeetingId, CancellationToken ct = default) =>
        throw NotConfigured();

    public Task<ProviderHealth> GetHealthAsync(CancellationToken ct = default) => Task.FromResult(new ProviderHealth(false, false));

    private static MeetingProviderException NotConfigured() => new(MeetingFailure.NotConfigured, "No meeting provider is configured.");
}
